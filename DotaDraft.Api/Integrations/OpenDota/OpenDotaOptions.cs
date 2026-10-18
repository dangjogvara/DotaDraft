namespace DotaDraft.Api.Integrations.OpenDota;

public class OpenDotaOptions
{
    public const string SectionName = "OpenDota";

    public string BaseUrl { get; set; } = "https://api.opendota.com/";

    public string SteamCdnBaseUrl { get; set; } = "https://cdn.cloudflare.steamstatic.com";

    /// <summary>Delay between per-hero matchup requests, used to stay under OpenDota's rate limit (60/minute).</summary>
    public int MatchupRequestDelayMs { get; set; } = 1100;

    /// <summary>Retries after a transient failure such as 429 Too Many Requests.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First retry delay; doubles (with jitter) on each further attempt.</summary>
    public int RetryBaseDelayMs { get; set; } = 5000;

    /// <summary>Upper bound for a single retry delay. A Retry-After longer than this is not retried.</summary>
    public int RetryMaxDelayMs { get; set; } = 60000;
}
