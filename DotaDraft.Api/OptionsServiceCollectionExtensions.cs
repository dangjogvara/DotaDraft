using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;

namespace DotaDraft.Api;

public static class OptionsServiceCollectionExtensions
{
    // Validated at startup, so bad configuration fails fast instead of on the first request or seed
    public static IServiceCollection AddDotaDraftOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<OpenDotaOptions>(configuration, OpenDotaOptions.SectionName);
        services.AddValidatedOptions<MatchupOptions>(configuration, MatchupOptions.SectionName);
        services.AddValidatedOptions<CounterOptions>(configuration, CounterOptions.SectionName);
        services.AddValidatedOptions<SeedingOptions>(configuration, SeedingOptions.SectionName);
        return services;
    }

    private static void AddValidatedOptions<TOptions>(
        this IServiceCollection services, IConfiguration configuration, string sectionName) where TOptions : class =>
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
