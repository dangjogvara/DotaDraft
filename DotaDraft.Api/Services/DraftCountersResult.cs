using DotaDraft.Api.Dtos;

namespace DotaDraft.Api.Services;

/// <summary>Either the ranked counters, or the requested hero IDs (enemies or excluded) that aren't known heroes.</summary>
public record DraftCountersResult(IReadOnlyList<DraftCounterDto> Counters, IReadOnlyList<int> UnknownHeroIds);
