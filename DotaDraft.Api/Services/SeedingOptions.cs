using System.ComponentModel.DataAnnotations;

namespace DotaDraft.Api.Services;

public class SeedingOptions
{
    public const string SectionName = "Seeding";

    /// <summary>Hours between automatic seeds.</summary>
    [Range(1, 24 * 30)]
    public int IntervalHours { get; set; } = 24;

    /// <summary>Minutes before retrying a failed seed; doubles on each failure, up to <see cref="IntervalHours"/>.</summary>
    [Range(1, 24 * 60)]
    public int RetryDelayMinutes { get; set; } = 5;
}
