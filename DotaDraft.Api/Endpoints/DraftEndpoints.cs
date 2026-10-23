using System.ComponentModel.DataAnnotations;
using DotaDraft.Api.Services;

namespace DotaDraft.Api.Endpoints;

public static class DraftEndpoints
{
    public const int MaxEnemies = 5;
    public const int MaxLimit = 20;

    private const string LineupSizeError = "Pass 1 to 5 enemy hero IDs, e.g. ?against=1&against=14.";

    public static void RegisterDraftEndpoints(this WebApplication app)
    {
        // Repeated keys bind to the array: /api/counters?against=1&against=14
        app.MapGet("/api/counters", GetDraftCounters);
    }

    // The attributes are checked by the framework's validation (AddValidation) before the handler runs,
    // returning a 400 ValidationProblem; only the unknown-ID check needs the database, so CounterService does it
    private static async Task<IResult> GetDraftCounters(
        [Required(ErrorMessage = LineupSizeError)]
        [Length(1, MaxEnemies, ErrorMessage = LineupSizeError)]
        [Distinct(ErrorMessage = "Enemy hero IDs must be distinct.")]
        int[]? against,
        CounterService counters,
        [Range(1, MaxLimit)] int limit = 10,
        CancellationToken ct = default)
    {
        var result = await counters.GetDraftCountersAsync(against!, limit, ct);
        if (result.UnknownHeroIds.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["against"] = [$"Unknown hero IDs: {string.Join(", ", result.UnknownHeroIds)}."]
            });
        }

        return TypedResults.Ok(result.Counters);
    }
}
