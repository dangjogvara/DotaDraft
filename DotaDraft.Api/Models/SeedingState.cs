namespace DotaDraft.Api.Models;

// A single row remembering when the matchups were last seeded, so a restart can tell whether the data is fresh
public class SeedingState
{
    public const int RowId = 1;

    public int Id { get; set; } = RowId;
    public DateTimeOffset MatchupsSeededAt { get; set; }
}
