using System.Text.Json.Serialization;

namespace DotaDraft.Api.Integrations.OpenDota;

public record OpenDotaMatchIdRow(
    [property: JsonPropertyName("match_id")]
    long? MatchId
);
