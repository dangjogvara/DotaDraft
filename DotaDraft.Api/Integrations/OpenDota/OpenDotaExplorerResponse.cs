using System.Text.Json.Serialization;

namespace DotaDraft.Api.Integrations.OpenDota;

public record OpenDotaExplorerResponse<T>(
    [property: JsonPropertyName("rows")] List<T>? Rows
);
