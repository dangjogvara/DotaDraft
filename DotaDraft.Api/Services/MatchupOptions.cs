using System.ComponentModel.DataAnnotations;

namespace DotaDraft.Api.Services;

public class MatchupOptions
{
    public const string SectionName = "Matchups";

    /// <summary>Match IDs per explorer query. Much larger windows hit its ~15s timeout (200,000 took about 6s).</summary>
    [Range(1, int.MaxValue)]
    public int WindowSize { get; set; } = 200_000;

    /// <summary>Windows queried per seed, walking back from the newest match. 15 covers about 1.5 days of matches.</summary>
    [Range(1, 1_000)]
    public int WindowCount { get; set; } = 15;

    /// <summary>Optional minimum average rank tier (10–15 Herald … 80 Immortal); unset includes all ranks.</summary>
    [Range(10, 80)]
    public int? MinAvgRankTier { get; set; }
}
