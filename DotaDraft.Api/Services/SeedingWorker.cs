using DotaDraft.Api.Data;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

// Seeds every Seeding:IntervalHours counted from the last successful seed, which is stored in the database, so a
// restart only seeds straight away when the data is stale or missing. Failures retry with exponential backoff
public class SeedingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SeedingOptions> options,
    TimeProvider time,
    ILogger<SeedingWorker> logger) : BackgroundService
{
    private int _consecutiveFailures;

    private TimeSpan Interval => TimeSpan.FromHours(options.Value.IntervalHours);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                var wait = await RunIfDueAsync(stoppingToken);
                await Task.Delay(wait, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down
        }
    }

    // Returns how long to wait before checking again
    private async Task<TimeSpan> RunIfDueAsync(CancellationToken stoppingToken)
    {
        try
        {
            var timeUntilDue = await TimeUntilDueAsync(stoppingToken);
            if (timeUntilDue > TimeSpan.Zero)
            {
                _consecutiveFailures = 0;
                logger.LogInformation("Matchups are up to date; the next seed runs in {Hours:0.#} hours.", timeUntilDue.TotalHours);
                return timeUntilDue;
            }

            await SeedAsync(stoppingToken);
            _consecutiveFailures = 0;
            return Interval;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            var retryIn = RetryDelay(_consecutiveFailures);
            // Keep the worker alive. A failed matchup seed writes nothing, so the existing matchups stay in place
            logger.LogError(ex, "Seeding failed; the next try is in {Delay}.", FormatDelay(retryIn));
            return retryIn;
        }
    }

    private TimeSpan RetryDelay(int failureCount)
    {
        var delay = TimeSpan.FromMinutes(options.Value.RetryDelayMinutes);
        for (var i = 1; i < failureCount && delay < Interval; i++)
            delay += delay;
        return delay < Interval ? delay : Interval;
    }

    private static string FormatDelay(TimeSpan delay) =>
        delay >= TimeSpan.FromHours(1)
            ? $"{delay.TotalHours:0.#} {(delay.TotalHours == 1 ? "hour" : "hours")}"
            : $"{delay.TotalMinutes:0.#} {(delay.TotalMinutes == 1 ? "minute" : "minutes")}";

    private async Task<TimeSpan> TimeUntilDueAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.HeroMatchups.AnyAsync(ct))
            return TimeSpan.Zero;

        var state = await db.SeedingStates.FindAsync([SeedingState.RowId], ct);
        if (state is null)
            return TimeSpan.Zero;

        var dueAt = state.MatchupsSeededAt + Interval;
        var timeUntilDue = dueAt - time.GetUtcNow();
        return timeUntilDue > TimeSpan.Zero ? timeUntilDue : TimeSpan.Zero;
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        // HeroIngestionService and its AppDbContext are scoped, so each run gets its own scope
        await using var scope = scopeFactory.CreateAsyncScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<HeroIngestionService>();

        logger.LogInformation("Seeding heroes and matchups...");
        try
        {
            await ingestion.SeedHeroesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Heroes from an earlier seed are enough, so the matchup seed still runs
            logger.LogError(ex, "Seeding heroes failed; seeding matchups with the existing heroes.");
        }

        await ingestion.SeedMatchupsAsync(ct);
        await RecordSeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);
        logger.LogInformation("Seeding finished.");
    }

    // Only reached when the matchup seed succeeded
    private async Task RecordSeedAsync(AppDbContext db, CancellationToken ct)
    {
        var state = await db.SeedingStates.FindAsync([SeedingState.RowId], ct);
        if (state is null)
        {
            state = new SeedingState();
            db.SeedingStates.Add(state);
        }

        state.MatchupsSeededAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}
