using System.ComponentModel.DataAnnotations;

namespace DotaDraft.Api.Services;

public enum MatchupSource
{
    // Ranked All Pick public matches via OpenDota's explorer: thousands of games per matchup
    Public,
    // OpenDota's /heroes/{id}/matchups, which uses a small set of high-level matches
    Pro
}

public class MatchupOptions
{
    public const string SectionName = "Matchups";

    [EnumDataType(typeof(MatchupSource))]
    public MatchupSource Source { get; set; } = MatchupSource.Public;

    /// <summary>
    /// Match IDs per explorer query. Larger windows hit the explorer's ~15s statement timeout
    /// (200,000 IDs took about 6s when measured).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int WindowSize { get; set; } = 200_000;

    /// <summary>Windows queried per seed, walking back from the newest match. 15 covers about 1.5 days of matches.</summary>
    [Range(1, 1_000)]
    public int WindowCount { get; set; } = 15;

    /// <summary>
    /// Optional lower bound on a match's average rank tier (10-15 Herald ... 80 Immortal). Unset includes all ranks.
    /// </summary>
    [Range(10, 80)]
    public int? MinAvgRankTier { get; set; }
}
