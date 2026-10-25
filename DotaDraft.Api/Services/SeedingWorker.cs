using DotaDraft.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

// Seeds heroes and then matchups in the background: at startup if there are no matchups yet,
// then every Seeding:IntervalHours. A failed run is logged, and the next scheduled run tries again
public class SeedingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SeedingOptions> options,
    TimeProvider time,
    ILogger<SeedingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Created before the first run, so the interval counts from startup
        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.Value.IntervalHours), time);

        try
        {
            await RunAsync(onlyIfEmpty: true, stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunAsync(onlyIfEmpty: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down
        }
    }

    private async Task RunAsync(bool onlyIfEmpty, CancellationToken stoppingToken)
    {
        try
        {
            // HeroIngestionService and its AppDbContext are scoped, so each run gets its own scope
            await using var scope = scopeFactory.CreateAsyncScope();

            // Restarts don't re-seed data that's already there
            if (onlyIfEmpty && await scope.ServiceProvider.GetRequiredService<AppDbContext>().HeroMatchups.AnyAsync(stoppingToken))
            {
                logger.LogInformation("Matchups are already seeded; the next seed runs in {Hours} hours.", options.Value.IntervalHours);
                return;
            }

            logger.LogInformation("Seeding heroes and matchups...");
            var ingestion = scope.ServiceProvider.GetRequiredService<HeroIngestionService>();
            var heroes = await ingestion.SeedHeroesAsync(stoppingToken);
            // Runs even if the hero refresh failed, since heroes from an earlier seed are enough
            var matchups = await ingestion.SeedMatchupsAsync(stoppingToken);

            if (heroes.Succeeded && matchups.Succeeded)
                logger.LogInformation("Seeding finished: {Heroes} heroes and {Matchups} matchups inserted or updated.",
                    heroes.Saved, matchups.Saved);
            else
                logger.LogWarning("Seeding finished with errors: {Error}", heroes.Error ?? matchups.Error);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Keep the worker alive so the next scheduled run still happens
            logger.LogError(ex, "Seeding failed unexpectedly.");
        }
    }
}
