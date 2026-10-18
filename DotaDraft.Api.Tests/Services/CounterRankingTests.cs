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
}
