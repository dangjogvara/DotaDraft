using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;
using DotaDraft.Api.Services.MatchupSources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Services;

public class HeroIngestionRegistrationTests
{
    [Theory]
    [InlineData(MatchupSource.Public, typeof(PublicMatchupSource))]
    [InlineData(MatchupSource.Pro, typeof(ProMatchupSource))]
    public void MatchupSource_IsSelectedByConfiguration(MatchupSource source, Type expected)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()))
            .Configure<MatchupOptions>(o => o.Source = source)
            .AddSingleton<IOpenDotaClient>(new FakeOpenDotaClient())
            .AddHeroIngestion()
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IMatchupSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HeroIngestionService>());
    }
}
