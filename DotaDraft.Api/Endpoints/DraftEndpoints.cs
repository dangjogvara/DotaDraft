using System.ComponentModel.DataAnnotations;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DotaDraft.Api.Endpoints;

public static class DraftEndpoints
{
    public const int MaxEnemies = 5;
    public const int MaxExcluded = 5;
    public const int MaxLimit = 20;

    private const string LineupSizeError = "Pass 1 to 5 enemy hero IDs, e.g. ?against=1&against=14.";
    private const string ExcludeSizeError = "Pass at most 5 hero IDs to exclude, e.g. &exclude=5&exclude=8.";

    public static void RegisterDraftEndpoints(this WebApplication app)
    {
        // Repeated keys bind to the array: /api/counters?against=1&against=14
        app.MapGet("/api/counters", GetDraftCounters)
            .WithName(nameof(GetDraftCounters))
            .WithTags("Counters");
    }

    // The attributes are checked by the framework's validation (AddValidation) before the handler runs,
    // returning a 400 ValidationProblem. The overlap check spans two parameters, so the handler does it;
    // only the unknown-ID check needs the database, so CounterService does that one
    /// <summary>Best counter picks against an enemy lineup.</summary>
    /// <remarks>
    /// Ranked by each hero's advantage summed over the enemies, so heroes that counter several picks rank highest.
    /// Enemy heroes and excluded heroes are never suggested.
    /// </remarks>
    /// <param name="against">1 to 5 distinct enemy hero IDs. Repeat the key for each hero: against=1, then against=14.</param>
    /// <param name="exclude">
    /// Up to 5 distinct hero IDs that can't be picked, such as your own team's picks. None may also be in against.
    /// </param>
    /// <param name="limit">How many counters to return, 1 to 20.</param>
    internal static async Task<Results<Ok<IReadOnlyList<DraftCounterDto>>, ValidationProblem>> GetDraftCounters(
        [Required(ErrorMessage = LineupSizeError)]
        [Length(1, MaxEnemies, ErrorMessage = LineupSizeError)]
        [Distinct(ErrorMessage = "Enemy hero IDs must be distinct.")]
        int[]? against,
        [Length(0, MaxExcluded, ErrorMessage = ExcludeSizeError)]
        [Distinct(ErrorMessage = "Excluded hero IDs must be distinct.")]
        int[]? exclude,
        CounterService counters,
        [Range(1, MaxLimit)] int limit = 10,
        CancellationToken ct = default)
    {
        exclude ??= [];
        var inBoth = against!.Intersect(exclude).ToList();
        if (inBoth.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["exclude"] = [$"Hero IDs can't be both enemies and excluded: {string.Join(", ", inBoth)}."]
            });
        }

        var result = await counters.GetDraftCountersAsync(against, exclude, limit, ct);
        if (result.UnknownHeroIds.Count > 0)
        {
            // Reported under the parameter each unknown ID came from
            var errors = new Dictionary<string, string[]>();
            foreach (var (name, ids) in new[] { ("against", against), ("exclude", exclude) })
            {
                var unknown = ids.Where(result.UnknownHeroIds.Contains).ToList();
                if (unknown.Count > 0)
                    errors[name] = [$"Unknown hero IDs: {string.Join(", ", unknown)}."];
            }
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(result.Counters);
    }
}
