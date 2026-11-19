using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Tests;

// Invalid settings stop the app at startup rather than failing on the first request or seed
public class OptionsValidationTests
{
    // Runs the same checks the host runs at startup (ValidateOnStart), without starting the app. Starting
    // it with WebApplicationFactory made the failure race with the host's shutdown (#87)
    private static void ValidateAtStartup(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        using var services = new ServiceCollection().AddDotaDraftOptions(configuration).BuildServiceProvider();
        services.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("OpenDota:BaseUrl", "not a url")]
    [InlineData("OpenDota:MaxRetryAttempts", "-1")]
    [InlineData("Matchups:WindowSize", "0")]
    [InlineData("Matchups:WindowCount", "0")]
    [InlineData("Matchups:MinAvgRankTier", "5")]
    [InlineData("Counters:PriorGames", "-1")]
    [InlineData("Seeding:IntervalHours", "0")]
    [InlineData("Seeding:RetryDelayMinutes", "0")]
    public void InvalidSetting_FailsStartup(string key, string value)
    {
        var ex = Assert.Throws<OptionsValidationException>(() => ValidateAtStartup((key, value)));
        Assert.Contains(key.Split(':')[1], ex.Message);
    }

    [Fact]
    public void DefaultSettings_PassValidation()
    {
        ValidateAtStartup();
    }

    [Fact]
    public async Task DefaultSettings_StartUp()
    {
        await using var factory = await DotaDraftApiFactory.CreateAsync();

        factory.CreateClient();
    }
}
