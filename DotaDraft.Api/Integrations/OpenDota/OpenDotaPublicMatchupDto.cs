using System.Text.Json.Serialization;

namespace DotaDraft.Api.Integrations.OpenDota;

public record OpenDotaPublicMatchupDto(
    [property: JsonPropertyName("hero_id")]
    int HeroId,
    [property: JsonPropertyName("against_hero_id")]
    int AgainstHeroId,
    [property: JsonPropertyName("games")] int Games,
    [property: JsonPropertyName("wins")] int Wins
);
