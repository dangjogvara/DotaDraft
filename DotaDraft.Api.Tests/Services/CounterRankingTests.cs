using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;

namespace DotaDraft.Api.Tests.Services;

public class CounterRankingTests
{
    [Fact]
    public void LargeSample_BeatsLuckySmallSample()
    {
        var lucky = CounterRanking.Advantage(wins: 5, gamesPlayed: 6, expectedWinRate: 0.5, priorGames: 30);
        var solid = CounterRanking.Advantage(wins: 600, gamesPlayed: 1000, expectedWinRate: 0.5, priorGames: 30);

        Assert.True(solid > lucky);
    }

    [Fact]
    public void SubtractsExpectedWinRate_SoNoCounterEffectScoresNothing()
    {
        var advantage = CounterRanking.Advantage(wins: 600, gamesPlayed: 1000, expectedWinRate: 0.6, priorGames: 30);

        Assert.Equal(0, advantage, precision: 10);
    }

    [Fact]
    public void ApproachesRawAdvantage_ForLargeSamples()
    {
        var advantage = CounterRanking.Advantage(wins: 55_000, gamesPlayed: 100_000, expectedWinRate: 0.5, priorGames: 30);

        Assert.Equal(0.05, advantage, precision: 3);
    }

    [Fact]
    public void HigherPriorGames_ShrinkMore()
    {
        var weak = CounterRanking.Advantage(wins: 25, gamesPlayed: 35, expectedWinRate: 0.5, priorGames: 10);
        var strong = CounterRanking.Advantage(wins: 25, gamesPlayed: 35, expectedWinRate: 0.5, priorGames: 50);

        Assert.True(weak > strong);
        Assert.True(strong > 0);
    }

    [Fact]
    public void ReturnsZero_WithNoGamesAndNoPrior()
    {
        Assert.Equal(0, CounterRanking.Advantage(wins: 0, gamesPlayed: 0, expectedWinRate: 0.5, priorGames: 0));
    }

    [Fact]
    public void ExpectedWinRate_IsTheHerosBaseline_AgainstAnAverageEnemy()
    {
        Assert.Equal(0.53, CounterRanking.ExpectedWinRate(baselineWinRate: 0.53, enemyBaselineWinRate: 0.5), precision: 10);
    }

    [Fact]
    public void ExpectedWinRate_MovesByTheEnemysDistanceFrom50()
    {
        // The issue's example: Wraith King (55.34%) against Shadow Demon (42.63%)
        Assert.Equal(0.6271, CounterRanking.ExpectedWinRate(baselineWinRate: 0.5534, enemyBaselineWinRate: 0.4263), precision: 10);
        Assert.Equal(0.4466, CounterRanking.ExpectedWinRate(baselineWinRate: 0.5, enemyBaselineWinRate: 0.5534), precision: 10);
    }

    [Fact]
    public void ExpectedWinRate_StaysBetween0And1()
    {
        Assert.Equal(1, CounterRanking.ExpectedWinRate(baselineWinRate: 0.9, enemyBaselineWinRate: 0.2));
        Assert.Equal(0, CounterRanking.ExpectedWinRate(baselineWinRate: 0.1, enemyBaselineWinRate: 0.8));
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

    // Enemies without a stored baseline: the 50% default, which leaves the expected win rate at the hero's baseline
    private static readonly Dictionary<int, double> NoBaselines = [];

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
            [Weaver], [1], NoBaselines, [Matchup(Weaver, against: 1, wins: 60, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(10m, score.TotalAdvantage);
        Assert.Equal(50m, score.BaselineWinRate);
        Assert.Equal(new EnemyMatchupDto(1, 10m, 60m, 50m, 100), Assert.Single(score.Matchups));
    }

    [Fact]
    public void Score_ShrinksSmallSamples_WithPriorGames()
    {
        // 100 imaginary games at the baseline halve a 100-game edge
        var scores = CounterRanking.Score(
            [Weaver], [1], NoBaselines, [Matchup(Weaver, against: 1, wins: 60, games: 100)], priorGames: 100);

        Assert.Equal(5m, Assert.Single(scores).TotalAdvantage);
    }

    [Fact]
    public void Score_SumsTheAdvantages_OverTheEnemies()
    {
        var scores = CounterRanking.Score(
            [Weaver], [1, 14], NoBaselines,
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
            [Weaver], [1, 14, 5], NoBaselines,
            [Matchup(Weaver, against: 1, wins: 60, games: 100), Matchup(Weaver, against: 5, wins: 0, games: 0)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(new EnemyMatchupDto(14, 0m, null, null, 0), score.Matchups[1]);
        Assert.Equal(new EnemyMatchupDto(5, 0m, null, null, 0), score.Matchups[2]);
        Assert.Equal(10m, score.TotalAdvantage);
    }

    [Fact]
    public void Score_UsesTheHerosStoredBaseline()
    {
        // 60 of 100 for a hero that wins 40% in general: a raw edge of 20 points
        var weakWeaver = NewHero(63, "Weaver", baselineWinRate: 0.4);

        var scores = CounterRanking.Score(
            [weakWeaver], [1], NoBaselines, [Matchup(weakWeaver, against: 1, wins: 60, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(40m, score.BaselineWinRate);
        Assert.Equal(20m, score.TotalAdvantage);
    }

    [Fact]
    public void Score_KeepsTheOrderOfHeroesAndEnemies()
    {
        var scores = CounterRanking.Score(
            [Lion, Weaver], [14, 1], NoBaselines,
            [Matchup(Weaver, against: 1, wins: 70, games: 100), Matchup(Lion, against: 14, wins: 55, games: 100)], priorGames: 0);

        // Not sorted by advantage: ranking is the caller's job
        Assert.Equal([Lion.Id, Weaver.Id], scores.Select(s => s.Id));
        Assert.All(scores, s => Assert.Equal([14, 1], s.Matchups.Select(m => m.EnemyId)));
    }

    [Fact]
    public void Score_IgnoresMatchupsOfOtherHeroesAndEnemies()
    {
        var scores = CounterRanking.Score(
            [Weaver], [1], NoBaselines,
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
            [hopelessWeaver], [1], NoBaselines, [Matchup(hopelessWeaver, against: 1, wins: 1, games: 3)], priorGames: 0);

        Assert.Equal(33.33m, Assert.Single(scores).TotalAdvantage);
    }

    [Fact]
    public void Score_CopiesTheHerosDetails()
    {
        var score = Assert.Single(CounterRanking.Score(
            [Weaver], [1], NoBaselines, [], priorGames: 0));

        Assert.Equal((63, "Weaver", "agi", "https://cdn/63.png"), (score.Id, score.LocalizedName, score.PrimaryAttr, score.ImgUrl));
    }

    [Theory]
    [InlineData(0.42)]
    [InlineData(0.5)]
    [InlineData(0.56)]
    public void Score_GivesNoAdvantage_WithoutACounterEffect_HoweverStrongTheEnemy(double enemyBaseline)
    {
        // A 50% hero against an enemy that wins enemyBaseline in general wins 1 − enemyBaseline: just what their
        // strengths predict. Before the enemy's baseline was used this scored +8 against the weak enemy, −6 against the strong one
        var games = 10_000;
        var wins = (int)Math.Round(games * (1 - enemyBaseline));

        var scores = CounterRanking.Score(
            [Weaver], [1], new Dictionary<int, double> { [1] = enemyBaseline },
            [Matchup(Weaver, against: 1, wins: wins, games: games)], priorGames: 30);

        Assert.Equal(0m, Assert.Single(scores).TotalAdvantage);
    }

    [Fact]
    public void Score_AppliesEachEnemysOwnBaseline()
    {
        // 60 of 100 against each: expected 60% vs the weak enemy 1 (40%), 50% vs the average enemy 14,
        // and 40% vs the strong enemy 5 (60%)
        var scores = CounterRanking.Score(
            [Weaver], [1, 14, 5], new Dictionary<int, double> { [1] = 0.4, [14] = 0.5, [5] = 0.6 },
            [Matchup(Weaver, against: 1, wins: 60, games: 100), Matchup(Weaver, against: 14, wins: 60, games: 100),
             Matchup(Weaver, against: 5, wins: 60, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal([0m, 10m, 20m], score.Matchups.Select(m => m.Advantage));
        Assert.Equal([60m, 50m, 40m], score.Matchups.Select(m => m.ExpectedWinRate));
        Assert.Equal(30m, score.TotalAdvantage);
    }

    [Fact]
    public void Score_CombinesTheHerosAndTheEnemysBaselines()
    {
        // A 55% hero against a 45% enemy expects 60%, so 70 of 100 is a 10-point edge
        var strongWeaver = NewHero(63, "Weaver", baselineWinRate: 0.55);

        var scores = CounterRanking.Score(
            [strongWeaver], [1], new Dictionary<int, double> { [1] = 0.45 },
            [Matchup(strongWeaver, against: 1, wins: 70, games: 100)], priorGames: 0);

        var score = Assert.Single(scores);
        Assert.Equal(10m, score.TotalAdvantage);
        Assert.Equal(60m, Assert.Single(score.Matchups).ExpectedWinRate);
    }
}
