using System.Net;
using System.Text.Json.Nodes;

namespace DotaDraft.Api.Tests.Endpoints;

public class OpenApiDocumentTests : IAsyncLifetime
{
    private readonly DotaDraftApiFactory _factory = new();
    private JsonNode _document = null!;

    public async Task InitializeAsync() =>
        _document = JsonNode.Parse(await _factory.CreateClient().GetStringAsync("/openapi/v1.json"))!;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private JsonNode Operation(string path, string method = "get") => _document["paths"]![path]![method]!;

    private static string? SchemaRef(JsonNode operation, string status, string contentType = "application/json")
    {
        var schema = operation["responses"]![status]!["content"]![contentType]!["schema"]!;
        return (string?)(schema["items"] ?? schema)["$ref"];
    }

    public static TheoryData<string, string, string, string> Operations => new()
    {
        { "/api/heroes", "get", "GetHeroes", "Heroes" },
        { "/api/heroes/{id}/counters", "get", "GetHeroCounters", "Counters" },
        { "/api/counters", "get", "GetDraftCounters", "Counters" },
        { "/api/admin/seed-heroes", "post", "SeedHeroes", "Admin" },
        { "/api/admin/seed-matchups", "post", "SeedMatchups", "Admin" },
        { "/api/admin/seed-matchups/status", "get", "GetMatchupSeedStatus", "Admin" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryEndpoint_HasNameTagAndSummary(string path, string method, string operationId, string tag)
    {
        var operation = Operation(path, method);

        Assert.Equal(operationId, (string?)operation["operationId"]);
        Assert.Equal([tag], operation["tags"]!.AsArray().Select(t => (string?)t));
        Assert.False(string.IsNullOrWhiteSpace((string?)operation["summary"]));
    }

    [Fact]
    public void DraftCounters_DescribesSuccessAndValidationResponses()
    {
        var operation = Operation("/api/counters");

        Assert.Equal("#/components/schemas/DraftCounterDto", SchemaRef(operation, "200"));
        Assert.Equal("#/components/schemas/HttpValidationProblemDetails",
            SchemaRef(operation, "400", "application/problem+json"));
        Assert.Contains("Repeat the key for each hero",
            (string?)operation["parameters"]!.AsArray().Single(p => (string?)p!["name"] == "against")!["description"]);
    }

    [Fact]
    public void HeroCounters_DescribesNotFound()
    {
        var operation = Operation("/api/heroes/{id}/counters");

        Assert.Equal("#/components/schemas/HeroCounterDto", SchemaRef(operation, "200"));
        Assert.Equal("#/components/schemas/ProblemDetails", SchemaRef(operation, "404", "application/problem+json"));
    }

    [Theory]
    [InlineData("/api/admin/seed-heroes", "post")]
    [InlineData("/api/admin/seed-matchups", "post")]
    [InlineData("/api/admin/seed-matchups/status", "get")]
    public void AdminEndpoints_DescribeUnauthorized(string path, string method)
    {
        Assert.NotNull(Operation(path, method)["responses"]!["401"]);
    }

    [Fact]
    public void SeedMatchups_DescribesAcceptedStatusAndConflict()
    {
        var operation = Operation("/api/admin/seed-matchups", "post");

        Assert.Equal("#/components/schemas/MatchupSeedStatusDto", SchemaRef(operation, "202"));
        Assert.NotNull(operation["responses"]!["409"]);
    }

    [Fact]
    public void DtoProperties_HaveDescriptions_AndPlainNumberTypes()
    {
        var properties = _document["components"]!["schemas"]!["HeroCounterDto"]!["properties"]!;

        Assert.Contains("percentage points", (string?)properties["advantage"]!["description"]);
        Assert.Equal("integer", (string?)properties["gamesPlayed"]!["type"]);
        Assert.Equal("number", (string?)properties["advantage"]!["type"]);
    }

    [Fact]
    public async Task ApiReference_IsServedInDevelopment()
    {
        var response = await _factory.CreateClient().GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DocumentAndApiReference_AreNotServedInProduction()
    {
        await using var factory = new DotaDraftApiFactory(environment: "Production");
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar")).StatusCode);
    }
}
