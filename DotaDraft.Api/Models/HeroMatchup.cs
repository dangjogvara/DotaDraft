namespace DotaDraft.Api.Models;

public class HeroMatchup
{
    public int HeroId { get; set; }
    public Hero Hero { get; set; } = null!;

    public int AgainstHeroId { get; set; }
    public Hero AgainstHero { get; set; } = null!;

    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public decimal WinRate { get; set; }
}