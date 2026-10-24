using DotaDraft.Api.Endpoints;

namespace DotaDraft.Api.Authentication;

public static class AdminKeyServiceCollectionExtensions
{
    // Registers the AdminKey scheme and the Admin policy that admin endpoints require
    public static IServiceCollection AddAdminKeyAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AdminKeyAuthenticationOptions, AdminKeyAuthenticationHandler>(AdminKeyDefaults.SchemeName, null);

        // Read from configuration when the handler first runs, so test and environment overrides apply
        services.AddOptions<AdminKeyAuthenticationOptions>(AdminKeyDefaults.SchemeName)
            .Configure<IConfiguration>((options, config) => options.ApiKey = config[AdminEndpoints.AdminKeyConfigKey]);

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminKeyDefaults.PolicyName, policy => policy
                .AddAuthenticationSchemes(AdminKeyDefaults.SchemeName)
                .RequireAuthenticatedUser());

        return services;
    }
}
