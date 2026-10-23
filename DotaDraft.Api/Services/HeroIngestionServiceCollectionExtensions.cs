using DotaDraft.Api.Services.MatchupSources;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

public static class HeroIngestionServiceCollectionExtensions
{
    // Registers HeroIngestionService with the matchup source named by Matchups:Source
    public static IServiceCollection AddHeroIngestion(this IServiceCollection services)
    {
        services.AddKeyedScoped<IMatchupSource, PublicMatchupSource>(MatchupSource.Public);
        services.AddKeyedScoped<IMatchupSource, ProMatchupSource>(MatchupSource.Pro);
        services.AddScoped(sp => sp.GetRequiredKeyedService<IMatchupSource>(
            sp.GetRequiredService<IOptions<MatchupOptions>>().Value.Source));

        services.AddScoped<HeroIngestionService>();
        return services;
    }
}
