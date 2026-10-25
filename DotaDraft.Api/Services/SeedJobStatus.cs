namespace DotaDraft.Api.Services;

public record SeedJobStatus(
    SeedJobState State,
    DateTimeOffset? QueuedAt = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null,
    SeedResult? Result = null,
    string? Error = null);
