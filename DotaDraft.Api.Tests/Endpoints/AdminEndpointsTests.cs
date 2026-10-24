using System.Net;
using DotaDraft.Api.Authentication;
using DotaDraft.Api.Integrations.OpenDota;

namespace DotaDraft.Api.Tests.Endpoints;

public class AdminEndpointsTests
{
    private static HttpRequestMessage Request(HttpMethod method, string url, string? adminKey)
    {
        var request = new HttpRequestMessage(method, url);
        if (adminKey is not null)
            request.Headers.Add(AdminKeyDefaults.HeaderName, adminKey);
        return request;
    }

    [Theory]
    [InlineData("POST", "/api/admin/seed-heroes")]
    [InlineData("POST", "/api/admin/seed-matchups")]
    [InlineData("GET", "/api/admin/seed-matchups/status")]
    public async Task ReturnsUnauthorized_WithoutKey(string method, string url)
    {
        await using var factory = new DotaDraftApiFactory();

        var response = await factory.CreateClient().SendAsync(Request(new HttpMethod(method), url, adminKey: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsUnauthorized_WithWrongKey_AndDoesNotSeed()
    {
        await using var factory = new DotaDraftApiFactory();
        factory.OpenDota.Heroes.Add(new OpenDotaHeroDto(1, "npc_dota_hero_1", "Hero 1", "str", [], null));

        var response = await factory.CreateClient()
            .SendAsync(Request(HttpMethod.Post, "/api/admin/seed-heroes", "wrong-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.OpenDota.HeroStatsRequests);
    }

    [Fact]
    public async Task RunsEndpoint_WithCorrectKey()
    {
        await using var factory = new DotaDraftApiFactory();
        factory.OpenDota.Heroes.Add(new OpenDotaHeroDto(1, "npc_dota_hero_1", "Hero 1", "str", [], null));

        var response = await factory.CreateClient()
            .SendAsync(Request(HttpMethod.Post, "/api/admin/seed-heroes", DotaDraftApiFactory.AdminKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.OpenDota.HeroStatsRequests);
    }

    [Fact]
    public async Task EndpointsAreNotMapped_WhenNoKeyConfigured()
    {
        await using var factory = new DotaDraftApiFactory(adminKey: null);

        var response = await factory.CreateClient()
            .SendAsync(Request(HttpMethod.Post, "/api/admin/seed-heroes", "anything"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
