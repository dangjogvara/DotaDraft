using DotaDraft.Api.Data;
using DotaDraft.Api.Endpoints;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Tests.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotaDraft.Api.Tests;

// Runs the real app in memory, with an in-memory database and a fake OpenDota client
public class DotaDraftApiFactory(string? adminKey = DotaDraftApiFactory.AdminKey, string environment = "Development")
    : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key";

    public FakeOpenDotaClient OpenDota { get; } = new();

    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Empty string overrides any key from user-secrets, so "no key" really means none
        builder.UseSetting(AdminEndpoints.AdminKeyConfigKey, adminKey ?? "");
        builder.UseEnvironment(environment);

        builder.ConfigureTestServices(services =>
        {
            // Replace the Npgsql registration; leaving it in place would register two providers
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

            services.RemoveAll<IOpenDotaClient>();
            services.AddSingleton<IOpenDotaClient>(OpenDota);
        });
    }
}
