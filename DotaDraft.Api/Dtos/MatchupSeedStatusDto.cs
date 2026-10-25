namespace DotaDraft.Api.Dtos;

/// <summary>The latest matchup seeding job. Status is kept in memory, so it resets when the API restarts.</summary>
/// <param name="State">Idle, Queued, Running, Succeeded or Failed.</param>
/// <param name="QueuedAt">When the job was requested.</param>
/// <param name="StartedAt">When the background worker picked it up.</param>
/// <param name="FinishedAt">When it succeeded or failed.</param>
/// <param name="Saved">Matchup rows inserted or updated.</param>
/// <param name="FailedHeroIds">Heroes whose matchups couldn't be fetched or saved (Pro source).</param>
/// <param name="FailedWindows">Match ID windows that couldn't be fetched (Public source).</param>
/// <param name="Error">Why the job failed, if it did.</param>
public record MatchupSeedStatusDto(
    string State,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? Saved,
    IReadOnlyList<int>? FailedHeroIds,
    int? FailedWindows,
    string? Error
);
