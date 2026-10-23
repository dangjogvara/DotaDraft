using DotaDraft.Api.Models;

namespace DotaDraft.Api.Services.MatchupSources;

// Where matchup data comes from, chosen by Matchups:Source (see AddHeroIngestion)
public interface IMatchupSource
{
    /// <param name="heroes">Every hero in the database, ordered by ID; matchups with other heroes are skipped.</param>
    Task<SeedResult> SeedAsync(IReadOnlyList<Hero> heroes, CancellationToken ct);
}
