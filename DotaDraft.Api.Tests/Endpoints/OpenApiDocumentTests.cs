using System.Net;
using System.Text.Json.Nodes;

namespace DotaDraft.Api.Tests.Endpoints;

public class OpenApiDocumentTests : IAsyncLifetime
{
    private DotaDraftApiFactory _factory = null!;
    private JsonNode _document = null!;

    public async Task InitializeAsync()
    {
        _factory = await DotaDraftApiFactory.CreateAsync();
        _document = JsonNode.Parse(await _factory.CreateClient().GetStringAsync("/openapi/v1.json"))!;
    }

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
        { "/api/counters", "get", "GetDraftCounters", "Counters" },
        { "/api/team-matchup", "get", "GetTeamMatchup", "Counters" },
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

        Assert.Equal("#/components/schemas/HeroScoreDto", SchemaRef(operation, "200"));
        Assert.Equal("#/components/schemas/HttpValidationProblemDetails",
            SchemaRef(operation, "400", "application/problem+json"));
        Assert.Contains("Repeat the key for each hero",
            (string?)operation["parameters"]!.AsArray().Single(p => (string?)p!["name"] == "against")!["description"]);
        Assert.Contains("such as Carry or Support",
            (string?)operation["parameters"]!.AsArray().Single(p => (string?)p!["name"] == "role")!["description"]);
    }

    [Fact]
    public void TeamMatchup_DescribesSuccessAndValidationResponses()
    {
        var operation = Operation("/api/team-matchup");

        Assert.Equal("#/components/schemas/TeamMatchupDto", SchemaRef(operation, "200"));
        Assert.Equal("#/components/schemas/HttpValidationProblemDetails",
            SchemaRef(operation, "400", "application/problem+json"));
        Assert.Contains("isn't a win prediction", (string?)operation["description"]);
    }

    [Fact]
    public void DtoProperties_HaveDescriptions_AndPlainNumberTypes()
    {
        var properties = _document["components"]!["schemas"]!["EnemyMatchupDto"]!["properties"]!;

        Assert.Contains("percentage points", (string?)properties["advantage"]!["description"]);
        Assert.Contains("baselines", (string?)properties["expectedWinRate"]!["description"]);
        Assert.Equal("integer", (string?)properties["gamesPlayed"]!["type"]);
        Assert.Equal("number", (string?)properties["advantage"]!["type"]);
    }

    [Fact]
    public void HealthCheck_IsNotInTheDocument()
    {
        Assert.Null(_document["paths"]!["/health"]);
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
        await using var factory = await DotaDraftApiFactory.CreateAsync(environment: "Production");
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar")).StatusCode);
    }
}
