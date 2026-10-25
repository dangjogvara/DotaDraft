namespace DotaDraft.Api.Dtos;

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
