using System.ComponentModel.DataAnnotations;

namespace DotaDraft.Api.Integrations.OpenDota;

public class OpenDotaOptions
{
    public const string SectionName = "OpenDota";

    [Required, Url]
    public string BaseUrl { get; set; } = "https://api.opendota.com/";

    /// <summary>Optional key for higher rate limits. Keep it in user-secrets or the environment, not appsettings.</summary>
    public string? ApiKey { get; set; }

    [Required, Url]
    public string SteamCdnBaseUrl { get; set; } = "https://cdn.cloudflare.steamstatic.com";

    /// <summary>Retries after a transient failure such as 429 Too Many Requests.</summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First retry delay; doubles (with jitter) on each further attempt.</summary>
    [Range(0, int.MaxValue)]
    public int RetryBaseDelayMs { get; set; } = 5000;

    /// <summary>Upper bound for a single retry delay. A Retry-After longer than this is not retried.</summary>
    [Range(1, int.MaxValue)]
    public int RetryMaxDelayMs { get; set; } = 60000;
}
