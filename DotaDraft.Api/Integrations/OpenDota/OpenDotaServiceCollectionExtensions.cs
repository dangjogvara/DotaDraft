using System.Net.Http.Headers;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace DotaDraft.Api.Integrations.OpenDota;

public static class OpenDotaServiceCollectionExtensions
{
    public static IHttpClientBuilder AddOpenDotaClient(this IServiceCollection services)
    {
        var builder = services.AddHttpClient<IOpenDotaClient, OpenDotaClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OpenDotaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            // Polly enforces the per-attempt timeout below, so HttpClient's 100s default doesn't cut off retries
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("User-Agent", "DotaDraftApp/1.0");

            // Sent as a header rather than the api_key query parameter, so it stays out of logged request URLs
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        });

        // Retry transient failures (429, 408, 5xx, network errors, timeouts), honouring Retry-After when OpenDota sends it.
        // OpenDota limits free use to 60 requests/minute, so the backoff has to be able to outlast a minute window.
        builder.AddResilienceHandler("opendota", (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<OpenDotaOptions>>().Value;
            if (options.MaxRetryAttempts > 0)
            {
                var maxDelay = TimeSpan.FromMilliseconds(options.RetryMaxDelayMs);
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs),
                    MaxDelay = maxDelay,
                    ShouldRetryAfterHeader = true,
                    // MaxDelay doesn't apply to Retry-After. A longer wait than that (e.g. daily quota used up)
                    // won't clear mid-run, so fail fast instead of stalling the seed
                    ShouldHandle = args => ValueTask.FromResult(
                        HttpClientResiliencePredicates.IsTransient(args.Outcome)
                        && !(GetRetryAfter(args.Outcome.Result) > maxDelay))
                });
            }
            // Inner to retry so each attempt gets its own timeout and timed-out attempts are retried
            pipeline.AddTimeout(TimeSpan.FromMilliseconds(options.AttemptTimeoutMs));
        });

        return builder;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage? response) =>
        response?.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => null
        };
}
