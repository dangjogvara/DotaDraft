namespace DotaDraft.Api.Dtos;

/// <summary>A counter pick against one hero. Win rates and advantage are percentages.</summary>
/// <param name="Id">The counter hero's ID.</param>
/// <param name="LocalizedName">The counter hero's display name.</param>
/// <param name="PrimaryAttr">str, agi, int or all.</param>
/// <param name="ImgUrl">Portrait on Steam's CDN, if known.</param>
/// <param name="Advantage">
/// Sample-adjusted win rate against the enemy minus the counter hero's baseline, in percentage points.
/// Counters are ranked by this.
/// </param>
/// <param name="WinRate">Raw win rate against the enemy.</param>
/// <param name="BaselineWinRate">The counter hero's win rate across all its matchups.</param>
/// <param name="GamesPlayed">Games between the two heroes behind the numbers.</param>
public record HeroCounterDto(
    int Id,
    string LocalizedName,
    string PrimaryAttr,
    string? ImgUrl,
    decimal Advantage,
    decimal WinRate,
    decimal BaselineWinRate,
    int GamesPlayed
);
