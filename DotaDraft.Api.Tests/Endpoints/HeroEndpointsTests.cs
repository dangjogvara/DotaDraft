using System.Net;
using System.Net.Http.Json;
using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Endpoints;

public class HeroEndpointsTests : IAsyncLifetime
{
    private readonly DotaDraftApiFactory _factory = new();

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Heroes.AddRange(Enumerable.Range(1, 5).Select(id => new Hero
        {
            Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = $"Hero {id}", PrimaryAttr = "str"
        }));

        db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task GetHeroes_ReturnsHeroDtosSortedByName_WithoutMatchups()
    {
        var response = await _factory.CreateClient().GetAsync("/api/heroes");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("matchups", json, StringComparison.OrdinalIgnoreCase);

        var heroes = await response.Content.ReadFromJsonAsync<List<HeroDto>>();
        Assert.Equal(5, heroes!.Count);
        Assert.Equal(heroes.Select(h => h.LocalizedName).Order(), heroes.Select(h => h.LocalizedName));
        Assert.Equal("npc_dota_hero_1", heroes.Single(h => h.Id == 1).Name);
    }
}
