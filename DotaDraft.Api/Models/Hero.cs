namespace DotaDraft.Api.Models;

public class Hero
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string LocalizedName { get; set; }
    public required string PrimaryAttr { get; set; }
    public string[] Roles { get; set; } = [];
    public string? ImgUrl { get; set; }

    /// <summary>The baseline for a hero without games: no evidence that it's stronger or weaker than average.</summary>
    public const double DefaultBaselineWinRate = 0.5;

    /// <summary>The hero's win rate across all its matchups, as a fraction. Set by each matchup seed.</summary>
    public double BaselineWinRate { get; set; } = DefaultBaselineWinRate;

    public ICollection<HeroMatchup> Matchups { get; set; } = [];
}