using DotaDraft.Api.Dtos;

namespace DotaDraft.Api.Services;

/// <summary>Either your team's matchup, or the requested hero IDs (on either team) that aren't known heroes.</summary>
public record TeamMatchupResult(TeamMatchupDto? Matchup, IReadOnlyList<int> UnknownHeroIds);
