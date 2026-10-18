using System.Text.Json.Serialization;

namespace DotaDraft.Api.Integrations.OpenDota;

public record OpenDotaHeroDto(
    int Id,
    string Name,
    [property: JsonPropertyName("localized_name")]
    string LocalizedName,
    [property: JsonPropertyName("primary_attr")]
    string PrimaryAttr,
    string[] Roles,
    [property: JsonPropertyName("img")] string? Img
);

public record OpenDotaMatchupDto(
    [property: JsonPropertyName("hero_id")]
    int HeroId,
    [property: JsonPropertyName("games_played")]
    int GamesPlayed,
    [property: JsonPropertyName("wins")] int Wins
);

// One hero-vs-hero total from the public-match explorer query, from HeroId's side
public record OpenDotaPublicMatchupDto(
    [property: JsonPropertyName("hero_id")]
    int HeroId,
    [property: JsonPropertyName("against_hero_id")]
    int AgainstHeroId,
    [property: JsonPropertyName("games")] int Games,
    [property: JsonPropertyName("wins")] int Wins
);

// Envelope of /api/explorer responses
public record OpenDotaExplorerResponse<T>(
    [property: JsonPropertyName("rows")] List<T>? Rows
);

public record OpenDotaMatchIdRow(
    [property: JsonPropertyName("match_id")]
    long? MatchId
);
