namespace DotaDraft.Api.Dtos;

/// <summary>
/// A hero scored against a whole enemy lineup: a counter suggestion, or one of your own team's picks.
/// Win rates and advantages are percentages.
/// </summary>
/// <param name="Id">The hero's ID.</param>
/// <param name="LocalizedName">The hero's display name.</param>
/// <param name="PrimaryAttr">str, agi, int or all.</param>
/// <param name="ImgUrl">Portrait on Steam's CDN, if known.</param>
/// <param name="TotalAdvantage">The per-enemy advantages summed, in percentage points. Suggestions are ranked by this.</param>
/// <param name="BaselineWinRate">The hero's win rate across all its matchups.</param>
/// <param name="Matchups">One entry per enemy, in the order requested.</param>
public record HeroScoreDto(
    int Id,
    string LocalizedName,
    string PrimaryAttr,
    string? ImgUrl,
    decimal TotalAdvantage,
    decimal BaselineWinRate,
    IReadOnlyList<EnemyMatchupDto> Matchups
);
