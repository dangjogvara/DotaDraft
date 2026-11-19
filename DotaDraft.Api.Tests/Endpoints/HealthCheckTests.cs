using System.Net;

namespace DotaDraft.Api.Tests.Endpoints;

public class HealthCheckTests
{
    [Fact]
    public async Task ReportsHealthy_WhenTheDatabaseIsReachable()
    {
        await using var factory = await DotaDraftApiFactory.CreateAsync();

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReportsUnhealthy_WhenTheDatabaseIsUnreachable()
    {
        await using var factory = await DotaDraftApiFactory.CreateAsync(unreachableDatabase: true);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
