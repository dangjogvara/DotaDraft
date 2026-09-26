namespace DotaDraft.Api.Services;

public class OpenDotaOptions
{
    public const string SectionName = "OpenDota";

    public string BaseUrl { get; set; } = "https://api.opendota.com/";

    public string SteamCdnBaseUrl { get; set; } = "https://cdn.cloudflare.steamstatic.com";

    /// <summary>Delay between per-hero matchup requests, used to stay under OpenDota's rate limit.</summary>
    public int MatchupRequestDelayMs { get; set; } = 1000;
}
