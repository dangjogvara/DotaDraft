using System.Text.Json.Serialization;

namespace DotaDraft.Api.Dtos;

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