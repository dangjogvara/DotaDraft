using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

public class HeroIngestionService(
    IOpenDotaClient openDota,
    AppDbContext db,
    ILogger<HeroIngestionService> logger,
    IOptions<OpenDotaOptions> options,
    IOptions<MatchupOptions> matchupOptions)
{
    private readonly OpenDotaOptions _options = options.Value;
    private readonly MatchupOptions _matchupOptions = matchupOptions.Value;

    public async Task<SeedResult> SeedHeroesAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching heroes from OpenDota API...");

        IReadOnlyList<OpenDotaHeroDto> heroDtos;
        try
        {
            heroDtos = await openDota.GetHeroStatsAsync(ct);
        }
        catch (OpenDotaException ex)
        {
            logger.LogError(ex, "Failed to retrieve heroes from OpenDota.");
            return SeedResult.Failed(SeedFailure.Upstream, "Failed to retrieve heroes from OpenDota.");
        }

        if (heroDtos.Count == 0)
        {
            logger.LogWarning("No heroes returned from OpenDota.");
            return SeedResult.Failed(SeedFailure.Upstream, "OpenDota returned no heroes.");
        }

        var existingHeroes = await db.Heroes.ToDictionaryAsync(h => h.Id, ct);

        foreach (var dto in heroDtos)
        {
            if (!existingHeroes.TryGetValue(dto.Id, out var hero))
            {
                hero = new Hero { Id = dto.Id, Name = dto.Name, LocalizedName = dto.LocalizedName, PrimaryAttr = dto.PrimaryAttr };
                db.Heroes.Add(hero);
            }

            // Overwrite existing heroes too, so renames and new art from a patch are picked up.
            // EF only issues UPDATEs for properties whose value actually changed.
            hero.Name = dto.Name;
            hero.LocalizedName = dto.LocalizedName;
            hero.PrimaryAttr = dto.PrimaryAttr;
            hero.Roles = dto.Roles;
            // Prefix relative path ("/apps/dota2/images/...png?") with Steam's CDN host. OpenDota ends paths
            // with an empty query string; trimming only a trailing '?' keeps any real query intact
            hero.ImgUrl = string.IsNullOrWhiteSpace(dto.Img) ? null : $"{_options.SteamCdnBaseUrl}{dto.Img.TrimEnd('?')}";
        }

        var (inserted, updated) = CountPendingWrites();
        if (inserted + updated > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Inserted {Inserted} and updated {Updated} heroes.", inserted, updated);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Failed to save heroes to the database.");
                // Detach the failed entities so later saves on this context don't retry them
                db.ChangeTracker.Clear();
                return SeedResult.Failed(SeedFailure.Database, "Failed to save heroes to the database.");
            }
        }
        else
        {
            logger.LogInformation("All heroes are already up to date.");
        }

        return SeedResult.Success(inserted + updated);
    }

    public async Task<SeedResult> SeedMatchupsAsync(CancellationToken ct = default)
    {
        // Ensure the Heroes table is populated
        var heroes = await db.Heroes.AsNoTracking().OrderBy(h => h.Id).ToListAsync(ct);
        if (heroes.Count == 0)
        {
            logger.LogWarning("Cannot seed matchups: Heroes table is empty. Seed heroes first.");
            return SeedResult.Failed(SeedFailure.MissingPrerequisite, "Heroes table is empty. Seed heroes first.");
        }

        return _matchupOptions.Source == MatchupSource.Pro
            ? await SeedProMatchupsAsync(heroes, ct)
            : await SeedPublicMatchupsAsync(heroes.Select(h => h.Id).ToHashSet(), ct);
    }

    // Sums hero-pair totals over consecutive match ID windows, newest first, then replaces all matchups
    // in one save. Nothing is written unless at least half the windows succeeded, so a bad run can't
    // swap good data for a tiny sample
    private async Task<SeedResult> SeedPublicMatchupsAsync(HashSet<int> validHeroIds, CancellationToken ct)
    {
        long latestMatchId;
        try
        {
            latestMatchId = await openDota.GetLatestPublicMatchIdAsync(ct);
        }
        catch (OpenDotaException ex)
        {
            logger.LogError(ex, "Failed to find the latest public match on OpenDota.");
            return SeedResult.Failed(SeedFailure.Upstream, "Failed to find the latest public match on OpenDota.");
        }

        var windowSize = _matchupOptions.WindowSize;
        var windowCount = _matchupOptions.WindowCount;
        var totals = new Dictionary<(int HeroId, int AgainstHeroId), (int Games, int Wins)>();
        var failedWindows = 0;

        logger.LogInformation(
            "Starting public matchup ingestion: {WindowCount} windows of {WindowSize} match IDs back from {LatestMatchId}...",
            windowCount, windowSize, latestMatchId);

        for (var i = 0; i < windowCount; i++)
        {
            // Rate limiting: the latest-match query above was the first request
            await Task.Delay(_options.MatchupRequestDelayMs, ct);

            var toMatchId = latestMatchId - (long)i * windowSize;
            var fromMatchId = toMatchId - windowSize + 1;

            IReadOnlyList<OpenDotaPublicMatchupDto> rows;
            try
            {
                rows = await openDota.GetPublicMatchupsAsync(fromMatchId, toMatchId, _matchupOptions.MinAvgRankTier, ct);
            }
            catch (OpenDotaException ex)
            {
                logger.LogError(ex, "Failed to retrieve public matchups for match IDs {From}-{To}", fromMatchId, toMatchId);
                failedWindows++;
                continue;
            }

            foreach (var row in rows)
            {
                if (!validHeroIds.Contains(row.HeroId) || !validHeroIds.Contains(row.AgainstHeroId)) continue;

                var (games, wins) = totals.GetValueOrDefault((row.HeroId, row.AgainstHeroId));
                totals[(row.HeroId, row.AgainstHeroId)] = (games + row.Games, wins + row.Wins);
            }

            logger.LogInformation("Fetched window {Window}/{WindowCount} ({Rows} hero pairs).", i + 1, windowCount, rows.Count);
        }

        var succeededWindows = windowCount - failedWindows;
        if (succeededWindows * 2 < windowCount || totals.Count == 0)
        {
            logger.LogError("Only {Succeeded} of {WindowCount} windows returned data; keeping existing matchups.",
                succeededWindows, windowCount);
            return SeedResult.Failed(SeedFailure.Upstream,
                    $"Only {succeededWindows} of {windowCount} match windows could be fetched; existing matchups were kept.")
                with { FailedWindows = failedWindows };
        }

        // Replace the table's contents: update or add every pair seen, and remove pairs that weren't,
        // so no rows from an older seed or the pro source linger
        var existingMatchups = await db.HeroMatchups.ToDictionaryAsync(m => (m.HeroId, m.AgainstHeroId), ct);
        foreach (var ((heroId, againstHeroId), (games, wins)) in totals)
        {
            if (!existingMatchups.TryGetValue((heroId, againstHeroId), out var matchup))
            {
                matchup = new HeroMatchup { HeroId = heroId, AgainstHeroId = againstHeroId };
                db.HeroMatchups.Add(matchup);
            }

            matchup.GamesPlayed = games;
            matchup.Wins = wins;
            matchup.WinRate = WinRate(wins, games);
        }

        var stale = existingMatchups.Where(e => !totals.ContainsKey(e.Key)).Select(e => e.Value).ToList();
        db.HeroMatchups.RemoveRange(stale);

        var (inserted, updated) = CountPendingWrites();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save public matchups to the database.");
            return SeedResult.Failed(SeedFailure.Database, "Failed to save matchups to the database.")
                with { FailedWindows = failedWindows };
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        logger.LogInformation(
            "Public matchup ingestion complete: {Pairs} pairs, {Inserted} inserted, {Updated} updated, {Removed} removed.",
            totals.Count, inserted, updated, stale.Count);

        if (failedWindows == 0)
            return SeedResult.Success(inserted + updated);

        return SeedResult.Failed(SeedFailure.Upstream,
                $"{failedWindows} of {windowCount} match windows failed; matchups were saved from the other {succeededWindows}.",
                inserted + updated)
            with { FailedWindows = failedWindows };
    }

    private async Task<SeedResult> SeedProMatchupsAsync(List<Hero> heroes, CancellationToken ct)
    {
        var validHeroIds = heroes.Select(h => h.Id).ToHashSet();
        int totalSaved = 0;
        var upstreamFailures = new List<int>();
        var databaseFailures = new List<int>();

        logger.LogInformation("Starting matchup ingestion for {Count} heroes...", heroes.Count);

        for (var i = 0; i < heroes.Count; i++)
        {
            var hero = heroes[i];

            // If the user cancelled or server is stopping, exit gracefully
            if (ct.IsCancellationRequested) break;

            // Rate limiting: wait before every request but the first, so the delay also
            // applies after a failed or empty response that skips the rest of the loop
            if (i > 0)
                await Task.Delay(_options.MatchupRequestDelayMs, ct);

            logger.LogInformation("Fetching matchups for {HeroName} ({HeroId})...", hero.LocalizedName, hero.Id);

            IReadOnlyList<OpenDotaMatchupDto> matchupDtos;
            try
            {
                matchupDtos = await openDota.GetHeroMatchupsAsync(hero.Id, ct);
            }
            catch (OpenDotaException ex)
            {
                logger.LogError(ex, "Failed to retrieve matchups for hero ID {HeroId}", hero.Id);
                upstreamFailures.Add(hero.Id);
                continue;
            }

            if (matchupDtos.Count == 0) continue;

            // Load this hero's existing matchups so they can be updated in place
            var existingMatchups = await db.HeroMatchups
                .Where(m => m.HeroId == hero.Id)
                .ToDictionaryAsync(m => m.AgainstHeroId, ct);

            foreach (var dto in matchupDtos)
            {
                // Skip opponents that don't exist in our database
                if (!validHeroIds.Contains(dto.HeroId)) continue;

                if (!existingMatchups.TryGetValue(dto.HeroId, out var matchup))
                {
                    matchup = new HeroMatchup { HeroId = hero.Id, AgainstHeroId = dto.HeroId };
                    db.HeroMatchups.Add(matchup);
                    existingMatchups[dto.HeroId] = matchup;
                }

                matchup.GamesPlayed = dto.GamesPlayed;
                matchup.Wins = dto.Wins;
                matchup.WinRate = WinRate(dto.Wins, dto.GamesPlayed);
            }

            var (inserted, updated) = CountPendingWrites();
            if (inserted + updated > 0)
            {
                try
                {
                    await db.SaveChangesAsync(ct);
                    totalSaved += inserted + updated;
                }
                catch (DbUpdateException ex)
                {
                    logger.LogError(ex, "Failed to save matchups for hero ID {HeroId}", hero.Id);
                    databaseFailures.Add(hero.Id);
                }
            }

            // Detach this hero's matchups: keeps the tracker small across the run,
            // and after a failure stops the next hero's save from retrying them
            db.ChangeTracker.Clear();
        }

        logger.LogInformation("Matchup ingestion complete. Total rows inserted or updated: {TotalSaved}", totalSaved);

        if (upstreamFailures.Count == 0 && databaseFailures.Count == 0)
            return SeedResult.Success(totalSaved);

        // A database failure is reported over an upstream one, since it points at our side
        var failure = databaseFailures.Count > 0 ? SeedFailure.Database : SeedFailure.Upstream;
        var failedHeroIds = upstreamFailures.Concat(databaseFailures).Order().ToList();
        return SeedResult.Failed(failure, $"Matchups failed for {failedHeroIds.Count} of {heroes.Count} heroes.", totalSaved)
            with { FailedHeroIds = failedHeroIds };
    }

    private static decimal WinRate(int wins, int games) =>
        games > 0 ? Math.Round((decimal)wins / games * 100m, 2) : 0m;

    // Entries() runs change detection, so modified properties on loaded entities are counted
    private (int Inserted, int Updated) CountPendingWrites()
    {
        var entries = db.ChangeTracker.Entries().ToList();
        return (entries.Count(e => e.State == EntityState.Added), entries.Count(e => e.State == EntityState.Modified));
    }
}