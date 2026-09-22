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