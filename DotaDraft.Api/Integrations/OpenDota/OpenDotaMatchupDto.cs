using System.Text.Json.Serialization;

namespace DotaDraft.Api.Integrations.OpenDota;

public record OpenDotaMatchupDto(
    [property: JsonPropertyName("hero_id")]
    int HeroId,
    [property: JsonPropertyName("games_played")]
    int GamesPlayed,
    [property: JsonPropertyName("wins")] int Wins
);
