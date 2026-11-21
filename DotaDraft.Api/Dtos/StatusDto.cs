namespace DotaDraft.Api.Dtos;

/// <summary>Whether the counter data is ready.</summary>
/// <param name="MatchupsSeededAt">
/// When the matchups were last seeded from OpenDota; null until the first seed finishes, so there are no counters yet.
/// </param>
public record StatusDto(DateTimeOffset? MatchupsSeededAt);
