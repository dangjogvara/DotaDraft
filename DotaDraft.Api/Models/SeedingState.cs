namespace DotaDraft.Api.Models;

// A single row (Id 1) remembering when the matchups were last seeded successfully, so a restart can tell
// whether the data is still fresh
public class SeedingState
{
    public const int RowId = 1;

    public int Id { get; set; } = RowId;
    public DateTimeOffset MatchupsSeededAt { get; set; }
}
