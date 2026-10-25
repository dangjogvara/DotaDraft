namespace DotaDraft.Api.Services;

public enum MatchupSource
{
    // Ranked All Pick public matches via OpenDota's explorer: thousands of games per matchup
    Public,
    // OpenDota's /heroes/{id}/matchups, which uses a small set of high-level matches
    Pro
}
