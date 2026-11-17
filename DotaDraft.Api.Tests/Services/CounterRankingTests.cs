using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;

namespace DotaDraft.Api.Tests.Services;

public class CounterRankingTests
{
    [Fact]
    public void LargeSample_BeatsLuckySmallSample()
    {
        var lucky = CounterRanking.Advantage(wins: 5, gamesPlayed: 6, baselineWinRate: 0.5, priorGames: 30);
        var solid = CounterRanking.Advantage(wins: 600, gamesPlayed: 1000, baselineWinRate: 0.5, priorGames: 30);

        Assert.True(solid > lucky);
    }

    [Fact]
    public void SubtractsBaseline_SoGenerallyStrongHeroScoresNothing()
    {
        var advantage = CounterRanking.Advantage(wins: 600, gamesPlayed: 1000, baselineWinRate: 0.6, priorGames: 30);

        Assert.Equal(0, advantage, precision: 10);
    }

    [Fact]
    public void ApproachesRawAdvantage_ForLargeSamples()
    {
        var advantage = CounterRanking.Advantage(wins: 55_000, gamesPlayed: 100_000, baselineWinRate: 0.5, priorGames: 30);

        Assert.Equal(0.05, advantage, precision: 3);
    }

    [Fact]
    public void HigherPriorGames_ShrinkMore()
    {
        var weak = CounterRanking.Advantage(wins: 25, gamesPlayed: 35, baselineWinRate: 0.5, priorGames: 10);
        var strong = CounterRanking.Advantage(wins: 25, gamesPlayed: 35, baselineWinRate: 0.5, priorGames: 50);

        Assert.True(weak > strong);
        Assert.True(strong > 0);
    }

    [Fact]
    public void ReturnsZero_WithNoGamesAndNoPrior()
    {
        Assert.Equal(0, CounterRanking.Advantage(wins: 0, gamesPlayed: 0, baselineWinRate: 0.5, priorGames: 0));
    }

    [Fact]
    public void BaselineWinRate_IsWinsOverGames()
    {
        Assert.Equal(0.45, CounterRanking.BaselineWinRate(wins: 90, gamesPlayed: 200), precision: 10);
    }

    [Fact]
    public void BaselineWinRate_IsTheDefault_WithoutGames()
    {
        Assert.Equal(Hero.DefaultBaselineWinRate, CounterRanking.BaselineWinRate(wins: 0, gamesPlayed: 0));
    }

    // Score: no database, just heroes (carrying their baselines) and matchups in memory

    private static readonly Hero Weaver = NewHero(63, "Weaver");
    private static readonly Hero Lion = NewHero(26, "Lion");

    private static Hero NewHero(int id, string name, double baselineWinRate = 0.5) => new()
    {
        Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = name, PrimaryAttr = "agi", ImgUrl = $"https://cdn/{id}.png",
        BaselineWinRate = baselineWinRate
    };

    private static HeroMatchup Matchup(Hero hero, int against, int wins, int games)
    {
        var matchup = new HeroMatchup { HeroId = hero.Id, AgainstHeroId = against };
        matchup.SetResults(games, wins);
        return matchup;
    }

    [Fact]
    public void Score_GivesAdvantageInPoints_AgainstTheHerosBaseline()
    {
        // 60 of 100 against a 50% baseline: a raw edge of 10 points
        var scores = CounterRanking.Score(
            [Weaver], [1], [Matchup(Weaver, against: 1, wins: 60, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(10m, score.TotalAdvantage);
        Assert.Equal(50m, score.BaselineWinRate);
        Assert.Equal(new EnemyMatchupDto(1, 10m, 60m, 100), Assert.Single(score.Matchups));
    }

    [Fact]
    public void Score_ShrinksSmallSamples_WithPriorGames()
    {
        // 100 imaginary games at the baseline halve a 100-game edge
        var scores = CounterRanking.Score(
            [Weaver], [1], [Matchup(Weaver, against: 1, wins: 60, games: 100)], priorGames: 100);

        Assert.Equal(5m, Assert.Single(scores).TotalAdvantage);
    }

    [Fact]
    public void Score_SumsTheAdvantages_OverTheEnemies()
    {
        var scores = CounterRanking.Score(
            [Weaver], [1, 14],
            [Matchup(Weaver, against: 1, wins: 60, games: 100), Matchup(Weaver, against: 14, wins: 45, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal([10m, -5m], score.Matchups.Select(m => m.Advantage));
        Assert.Equal(5m, score.TotalAdvantage);
    }

    [Fact]
    public void Score_CountsAnEnemyWithoutGames_AsZero()
    {
        // No matchup against 14, and one against 5 without games: neither is evidence either way
        var scores = CounterRanking.Score(
            [Weaver], [1, 14, 5],
            [Matchup(Weaver, against: 1, wins: 60, games: 100), Matchup(Weaver, against: 5, wins: 0, games: 0)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(new EnemyMatchupDto(14, 0m, null, 0), score.Matchups[1]);
        Assert.Equal(new EnemyMatchupDto(5, 0m, null, 0), score.Matchups[2]);
        Assert.Equal(10m, score.TotalAdvantage);
    }

    [Fact]
    public void Score_UsesTheHerosStoredBaseline()
    {
        // 60 of 100 for a hero that wins 40% in general: a raw edge of 20 points
        var weakWeaver = NewHero(63, "Weaver", baselineWinRate: 0.4);

        var scores = CounterRanking.Score(
            [weakWeaver], [1], [Matchup(weakWeaver, against: 1, wins: 60, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(40m, score.BaselineWinRate);
        Assert.Equal(20m, score.TotalAdvantage);
    }

    [Fact]
    public void Score_KeepsTheOrderOfHeroesAndEnemies()
    {
        var scores = CounterRanking.Score(
            [Lion, Weaver], [14, 1],
            [Matchup(Weaver, against: 1, wins: 70, games: 100), Matchup(Lion, against: 14, wins: 55, games: 100)], priorGames: 0);

        // Not sorted by advantage: ranking is the caller's job
        Assert.Equal([Lion.Id, Weaver.Id], scores.Select(s => s.Id));
        Assert.All(scores, s => Assert.Equal([14, 1], s.Matchups.Select(m => m.EnemyId)));
    }

    [Fact]
    public void Score_IgnoresMatchupsOfOtherHeroesAndEnemies()
    {
        var scores = CounterRanking.Score(
            [Weaver], [1],
            [Matchup(Weaver, against: 1, wins: 60, games: 100), Matchup(Weaver, against: 14, wins: 90, games: 100),
             Matchup(Lion, against: 1, wins: 90, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(10m, score.TotalAdvantage);
        Assert.Single(score.Matchups);
    }

    [Fact]
    public void Score_RoundsToTwoDecimals()
    {
        // 1 of 3 against a 0% baseline: 33.333... points
        var hopelessWeaver = NewHero(63, "Weaver", baselineWinRate: 0.0);

        var scores = CounterRanking.Score(
            [hopelessWeaver], [1], [Matchup(hopelessWeaver, against: 1, wins: 1, games: 3)], priorGames: 0);

        Assert.Equal(33.33m, Assert.Single(scores).TotalAdvantage);
    }

    [Fact]
    public void Score_CopiesTheHerosDetails()
    {
        var score = Assert.Single(CounterRanking.Score([Weaver], [1], [], priorGames: 0));

        Assert.Equal((63, "Weaver", "agi", "https://cdn/63.png"), (score.Id, score.LocalizedName, score.PrimaryAttr, score.ImgUrl));
    }
}
