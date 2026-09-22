namespace DotaDraft.Api.Models;

public class Hero
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string LocalizedName { get; set; }
    public required string PrimaryAttr { get; set; }
    public string[] Roles { get; set; } = [];
    public string? ImgUrl { get; set; }

    public ICollection<HeroMatchup> Matchups { get; set; } = [];
}