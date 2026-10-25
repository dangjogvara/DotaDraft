using System.Net;
using DotaDraft.Api.Data;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotaDraft.Api.Tests.Endpoints;

public class HealthCheckTests
{
    [Fact]
    public async Task ReportsHealthy_WhenTheDatabaseIsReachable()
    {
        await using var factory = new DotaDraftApiFactory();

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReportsUnhealthy_WhenTheDatabaseIsUnreachable()
    {
        await using var factory = new DotaDraftApiFactory();
        // Nothing listens on port 1, so the connection is refused straight away
        var client = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o =>
                o.UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2"));
        })).CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
