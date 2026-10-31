namespace DotaDraft.Api.Dtos;

/// <summary>
/// How your team's heroes do against the enemy lineup, from 1-vs-1 matchups only, so it isn't a win prediction.
/// Advantages are percentage points.
/// </summary>
/// <param name="TotalAdvantage">Every hero's total advantage summed: positive means your lineup tends to do well against theirs.</param>
/// <param name="Heroes">Your team's heroes in the order requested, each scored like a counter suggestion.</param>
public record TeamMatchupDto(decimal TotalAdvantage, IReadOnlyList<DraftCounterDto> Heroes);
