using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Services;

public class OpenDotaService(HttpClient httpClient, AppDbContext db, ILogger<OpenDotaService> logger)
{
    private const string BaseSteamCdn = "https://cdn.cloudflare.steamstatic.com";

    public async Task<int> SeedHeroesAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching heroes from OpenDota API...");

        var heroDtos = await httpClient.GetFromJsonAsync<List<OpenDotaHeroDto>>("api/heroStats", ct)
                       ?? [];

        if (heroDtos.Count == 0)
        {
            logger.LogWarning("No heroes returned from OpenDota.");
            return 0;
        }

        var existingIds = await db.Heroes
            .Select(h => h.Id)
            .ToHashSetAsync(ct);

        var newHeroes = heroDtos
            .Where(dto => !existingIds.Contains(dto.Id))
            .Select(dto => new Hero
            {
                Id = dto.Id,
                Name = dto.Name,
                LocalizedName = dto.LocalizedName,
                PrimaryAttr = dto.PrimaryAttr,
                Roles = dto.Roles,
                // Prefix relative path ("/apps/dota2/images/...") with Steam's CDN host
                ImgUrl = string.IsNullOrWhiteSpace(dto.Img) ? null : $"{BaseSteamCdn}{dto.Img}"
            })
            .ToList();

        if (newHeroes.Count > 0)
        {
            await db.Heroes.AddRangeAsync(newHeroes, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Saved {Count} new heroes to PostgreSQL.", newHeroes.Count);
        }
        else
        {
            logger.LogInformation("All heroes are already up to date.");
        }

        return newHeroes.Count;
    }
}