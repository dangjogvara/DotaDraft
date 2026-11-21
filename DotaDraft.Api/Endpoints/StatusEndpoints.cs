using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Endpoints;

public static class StatusEndpoints
{
    public static void RegisterStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", GetStatus)
            .WithName(nameof(GetStatus))
            .WithTags("Status");
    }

    /// <summary>Whether the counter data is ready.</summary>
    /// <remarks>
    /// On a fresh database the API seeds from OpenDota in the background, which takes about 1.5 minutes. Until the
    /// first seed finishes, matchupsSeededAt is null and the counter endpoints return no counters.
    /// </remarks>
    internal static async Task<Ok<StatusDto>> GetStatus(AppDbContext db, CancellationToken ct)
    {
        var seededAt = await db.SeedingStates
            .AsNoTracking()
            .Where(s => s.Id == SeedingState.RowId)
            .Select(s => (DateTimeOffset?)s.MatchupsSeededAt)
            .FirstOrDefaultAsync(ct);
        return TypedResults.Ok(new StatusDto(seededAt));
    }
}
