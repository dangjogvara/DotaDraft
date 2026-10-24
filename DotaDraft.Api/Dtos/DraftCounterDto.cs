namespace DotaDraft.Api.Dtos;

/// <summary>A counter pick against a whole enemy lineup. Win rates and advantages are percentages.</summary>
/// <param name="Id">The counter hero's ID.</param>
/// <param name="LocalizedName">The counter hero's display name.</param>
/// <param name="PrimaryAttr">str, agi, int or all.</param>
/// <param name="ImgUrl">Portrait on Steam's CDN, if known.</param>
/// <param name="TotalAdvantage">The per-enemy advantages summed, in percentage points. Suggestions are ranked by this.</param>
/// <param name="BaselineWinRate">The counter hero's win rate across all its matchups.</param>
/// <param name="Matchups">One entry per enemy, in the order requested.</param>
public record DraftCounterDto(
    int Id,
    string LocalizedName,
    string PrimaryAttr,
    string? ImgUrl,
    decimal TotalAdvantage,
    decimal BaselineWinRate,
    IReadOnlyList<EnemyMatchupDto> Matchups
);

/// <summary>The counter hero's results against one enemy.</summary>
/// <param name="EnemyId">The enemy hero's ID.</param>
/// <param name="Advantage">Sample-adjusted advantage in percentage points; 0 without matchup data.</param>
/// <param name="WinRate">Raw win rate against this enemy; null without matchup data.</param>
/// <param name="GamesPlayed">Games behind the numbers; 0 without matchup data.</param>
public record EnemyMatchupDto(
    int EnemyId,
    decimal Advantage,
    decimal? WinRate,
    int GamesPlayed
);
