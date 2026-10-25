using System.ComponentModel.DataAnnotations;

namespace DotaDraft.Api.Services;

public class SeedingOptions
{
    public const string SectionName = "Seeding";

    /// <summary>
    /// Hours between automatic seeds of heroes and matchups. At startup, a seed only runs if there are no matchups yet.
    /// </summary>
    [Range(1, 24 * 30)]
    public int IntervalHours { get; set; } = 24;
}
