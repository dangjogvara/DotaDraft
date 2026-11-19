using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;
using DotaDraft.Api.Tests.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotaDraft.Api.Tests;

// Runs the real app in memory, with its own empty Postgres database (see TestDatabase) and a fake OpenDota client
public class DotaDraftApiFactory : WebApplicationFactory<Program>
{
    // Nothing listens on port 1, so the connection is refused straight away
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";

    private readonly string _environment;
    private readonly string _connectionString;

    private DotaDraftApiFactory(string environment, string connectionString)
    {
        _environment = environment;
        _connectionString = connectionString;
    }

    public FakeOpenDotaClient OpenDota { get; } = new();

    // With unreachableDatabase, every database call fails instead, as if Postgres were down
    public static async Task<DotaDraftApiFactory> CreateAsync(
        string environment = "Development", bool unreachableDatabase = false) =>
        new(environment, unreachableDatabase ? UnreachableConnectionString : await TestDatabase.CreateAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureTestServices(services =>
        {
            // Replace the Npgsql registration; leaving it in place would register two providers
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connectionString));

            services.RemoveAll<IOpenDotaClient>();
            services.AddSingleton<IOpenDotaClient>(OpenDota);

            // Tests set up their own data; background seeding would race with it
            services.Remove(services.Single(d => d.ImplementationType == typeof(SeedingWorker)));
        });
    }
}
