using System.ComponentModel.DataAnnotations;

namespace DotaDraft.Api.Services;

public class CounterOptions
{
    public const string SectionName = "Counters";

    /// <summary>Imaginary games at the expected win rate added to every matchup; higher trusts small samples less.</summary>
    [Range(0, 100_000)]
    public int PriorGames { get; set; } = 30;
}
