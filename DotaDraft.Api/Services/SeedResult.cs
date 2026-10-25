namespace DotaDraft.Api.Services;

public record SeedResult(int Saved, SeedFailure? Failure = null, string? Error = null)
{
    // Public-match windows that could not be fetched; the matchups come from the others
    public int FailedWindows { get; init; }

    public bool Succeeded => Failure is null;

    public static SeedResult Success(int saved) => new(saved);

    public static SeedResult Failed(SeedFailure failure, string error, int saved = 0) => new(saved, failure, error);
}
