using DotaDraft.Api.Data;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

// Seeds heroes and then matchups in the background, every Seeding:IntervalHours counted from the last
// successful seed, which is stored in the database. So a restart only seeds straight away when the data is
// stale or missing. A failed run is logged, and the worker tries again after another interval
public class SeedingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SeedingOptions> options,
    TimeProvider time,
    ILogger<SeedingWorker> logger) : BackgroundService
{
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

    // Seeds if a seed is due, and returns how long to wait before checking again
    private async Task<TimeSpan> RunIfDueAsync(CancellationToken stoppingToken)
    {
        try
        {
            var timeUntilDue = await TimeUntilDueAsync(stoppingToken);
            if (timeUntilDue > TimeSpan.Zero)
            {
                logger.LogInformation("Matchups are up to date; the next seed runs in {Hours:0.#} hours.", timeUntilDue.TotalHours);
                return timeUntilDue;
            }

            await SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Keep the worker alive. A failed matchup seed writes nothing, so the existing matchups stay in place
            logger.LogError(ex, "Seeding failed; the next try is in {Hours} hours.", options.Value.IntervalHours);
        }

        // After a seed, or a failed one, the next is due one interval from now
        return Interval;
    }

    // Zero when a seed is due now: there are no matchups (e.g. a fresh database), no seed has been
    // recorded, or the last one finished more than an interval ago
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
