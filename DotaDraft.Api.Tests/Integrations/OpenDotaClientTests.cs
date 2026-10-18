using System.Diagnostics;
using System.Net;
using DotaDraft.Api.Integrations.OpenDota;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Integrations;

public class OpenDotaClientTests
{
    private const string MatchupsJson = """[{"hero_id":2,"games_played":10,"wins":5}]""";

    private readonly StubHandler _handler = new();

    // Uses the production registration so the retry pipeline under test is the real one
    private IOpenDotaClient CreateClient(int maxRetryAttempts = 3, int retryMaxDelayMs = 60000, string? apiKey = null) =>
        new ServiceCollection()
            .AddLogging()
            .Configure<OpenDotaOptions>(o =>
            {
                o.BaseUrl = "https://opendota.test/";
                o.MaxRetryAttempts = maxRetryAttempts;
                o.RetryBaseDelayMs = 1;
                o.RetryMaxDelayMs = retryMaxDelayMs;
                o.ApiKey = apiKey;
            })
            .AddOpenDotaClient()
            .ConfigurePrimaryHttpMessageHandler(() => _handler)
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IOpenDotaClient>();

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TooManyRequests(TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfter is not null)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
        return response;
    }

    [Fact]
    public async Task RetriesOn429_ThenReturnsData()
    {
        _handler.Responses.Enqueue(TooManyRequests());
        _handler.Responses.Enqueue(TooManyRequests());
        _handler.Responses.Enqueue(Json(MatchupsJson));

        var matchups = await CreateClient().GetHeroMatchupsAsync(1);

        Assert.Equal(3, _handler.Requests);
        Assert.Equal(2, Assert.Single(matchups).HeroId);
    }

    [Fact]
    public async Task RetriesOnServerError()
    {
        _handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.BadGateway));
        _handler.Responses.Enqueue(Json(MatchupsJson));

        await CreateClient().GetHeroMatchupsAsync(1);

        Assert.Equal(2, _handler.Requests);
    }

    [Fact]
    public async Task ThrowsOpenDotaException_WhenRetriesAreExhausted()
    {
        for (var i = 0; i < 3; i++)
            _handler.Responses.Enqueue(TooManyRequests());

        await Assert.ThrowsAsync<OpenDotaException>(() => CreateClient(maxRetryAttempts: 2).GetHeroMatchupsAsync(1));
        Assert.Equal(3, _handler.Requests);
    }

    [Fact]
    public async Task DoesNotRetryNotFound()
    {
        _handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<OpenDotaException>(() => CreateClient().GetHeroMatchupsAsync(1));
        Assert.Equal(1, _handler.Requests);
    }

    [Fact]
    public async Task HonoursRetryAfter()
    {
        _handler.Responses.Enqueue(TooManyRequests(TimeSpan.FromSeconds(1)));
        _handler.Responses.Enqueue(Json(MatchupsJson));

        var stopwatch = Stopwatch.StartNew();
        await CreateClient().GetHeroMatchupsAsync(1);

        // The configured base delay is 1 ms, so only Retry-After explains a wait this long
        Assert.True(stopwatch.ElapsedMilliseconds >= 900, $"Elapsed {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task DoesNotRetry_WhenRetryAfterExceedsMaxDelay()
    {
        _handler.Responses.Enqueue(TooManyRequests(TimeSpan.FromHours(1)));

        // Fails with a cancellation instead of hanging for an hour if the long Retry-After is honoured
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<OpenDotaException>(
            () => CreateClient(retryMaxDelayMs: 50).GetHeroMatchupsAsync(1, cts.Token));
        Assert.Equal(1, _handler.Requests);
    }

    [Fact]
    public async Task SendsApiKeyAsBearerToken_NotInUrl()
    {
        _handler.Responses.Enqueue(Json(MatchupsJson));

        await CreateClient(apiKey: "secret-key").GetHeroMatchupsAsync(1);

        var request = _handler.LastRequest!;
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("secret-key", request.Headers.Authorization?.Parameter);
        Assert.DoesNotContain("secret-key", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendsNoAuthorizationHeader_WithoutApiKey()
    {
        _handler.Responses.Enqueue(Json(MatchupsJson));

        await CreateClient().GetHeroMatchupsAsync(1);

        Assert.Null(_handler.LastRequest!.Headers.Authorization);
    }

    private class StubHandler : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Responses { get; } = new();
        public int Requests { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            LastRequest = request;
            return Task.FromResult(Responses.Dequeue());
        }
    }
}
