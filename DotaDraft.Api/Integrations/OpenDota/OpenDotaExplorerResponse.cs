using System.Text.Json.Serialization;

namespace DotaDraft.Api.Integrations.OpenDota;

// Envelope of /api/explorer responses
public record OpenDotaExplorerResponse<T>(
    [property: JsonPropertyName("rows")] List<T>? Rows
);
