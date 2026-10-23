namespace DotaDraft.Api.Services;

// Runs matchup seeding outside the HTTP request, since a full run takes minutes
// and would otherwise be cut off by proxy timeouts or client disconnects
public class MatchupSeedWorker(
    MatchupSeedJob job,
    IServiceScopeFactory scopeFactory,
    ILogger<MatchupSeedWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var _ in job.Requests.ReadAllAsync(stoppingToken))
            {
                await RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down while waiting for the next request
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        job.MarkRunning();
        logger.LogInformation("Matchup seeding job started.");

        try
        {
            // HeroIngestionService and its AppDbContext are scoped, so each run gets its own scope
            await using var scope = scopeFactory.CreateAsyncScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<HeroIngestionService>();

            var result = await ingestion.SeedMatchupsAsync(stoppingToken);

            // The pro source stops early and returns what it saved when cancelled between heroes
            if (stoppingToken.IsCancellationRequested)
                job.MarkFailed("Cancelled because the application is shutting down.", result);
            else
                job.MarkFinished(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            job.MarkFailed("Cancelled because the application is shutting down.");
        }
        catch (Exception ex)
        {
            // Keep the worker alive so later requests can still run
            logger.LogError(ex, "Matchup seeding job failed.");
            job.MarkFailed("Matchup seeding failed unexpectedly.");
            return;
        }

        logger.LogInformation("Matchup seeding job finished: {State}.", job.Status.State);
    }
}
