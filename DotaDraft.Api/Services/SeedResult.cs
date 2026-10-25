namespace DotaDraft.Api.Services;

public record SeedResult(int Saved, SeedFailure? Failure = null, string? Error = null)
{
    // Heroes whose matchups could not be fetched or saved; the rest of the run still completes
    public IReadOnlyList<int> FailedHeroIds { get; init; } = [];

    // Public-match windows that could not be fetched; the matchups come from the others
    public int FailedWindows { get; init; }

    public bool Succeeded => Failure is null;

    public static SeedResult Success(int saved) => new(saved);

    public static SeedResult Failed(SeedFailure failure, string error, int saved = 0) => new(saved, failure, error);
}
