using System.Net.Http.Json;
using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Endpoints;

public class StatusEndpointsTests : IAsyncLifetime
{
    private DotaDraftApiFactory _factory = null!;

    public async Task InitializeAsync() => _factory = await DotaDraftApiFactory.CreateAsync();

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private async Task<StatusDto> GetStatus()
    {
        var response = await _factory.CreateClient().GetAsync("/api/status");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StatusDto>())!;
    }

    [Fact]
    public async Task GetStatus_HasNoSeedTime_BeforeTheFirstSeed()
    {
        var status = await GetStatus();

        Assert.Null(status.MatchupsSeededAt);
    }

    [Fact]
    public async Task GetStatus_GivesWhenTheMatchupsWereLastSeeded()
    {
        var seededAt = new DateTimeOffset(2026, 11, 20, 21, 17, 30, TimeSpan.Zero);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SeedingStates.Add(new SeedingState { MatchupsSeededAt = seededAt });
            await db.SaveChangesAsync();
        }

        var status = await GetStatus();

        Assert.Equal(seededAt, status.MatchupsSeededAt);
    }
}
