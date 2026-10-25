using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Tests;

// Invalid settings stop the app at startup rather than failing on the first request or seed
public class OptionsValidationTests
{
    [Theory]
    [InlineData("OpenDota:BaseUrl", "not a url")]
    [InlineData("OpenDota:MaxRetryAttempts", "-1")]
    [InlineData("Matchups:WindowSize", "0")]
    [InlineData("Matchups:WindowCount", "0")]
    [InlineData("Matchups:MinAvgRankTier", "5")]
    [InlineData("Counters:PriorGames", "-1")]
    [InlineData("Seeding:IntervalHours", "0")]
    public void InvalidSetting_FailsStartup(string key, string value)
    {
        using var factory = new DotaDraftApiFactory().WithWebHostBuilder(b => b.UseSetting(key, value));

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains(key.Split(':')[1], ex.Message);
    }

    [Fact]
    public void DefaultSettings_StartUp()
    {
        using var factory = new DotaDraftApiFactory();

        factory.CreateClient();
    }
}
