using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace DotaDraft.Api.Tests.Endpoints;

// Every error, whether returned explicitly, an empty status code or an unhandled exception, is ProblemDetails
public class ErrorResponseTests
{
    // Part of the connection failure's message, so it shows whether exception details leaked
    private const string InternalDetail = "127.0.0.1:1";

    private static async Task<ProblemDetails> AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)status, problem!.Status);
        return problem;
    }

    // Asks for JSON, so the developer exception page answers with ProblemDetails rather than HTML
    private static Task<HttpResponseMessage> GetHeroes(DotaDraftApiFactory factory)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/heroes");
        request.Headers.Accept.ParseAdd("application/json");
        return factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task UnknownHero_ReturnsNotFoundProblem()
    {
        await using var factory = new DotaDraftApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/heroes/999/counters");

        var problem = await AssertProblem(response, HttpStatusCode.NotFound);
        Assert.Equal("Hero not found", problem.Title);
        Assert.Equal("Hero with ID 999 not found.", problem.Detail);
    }

    [Theory]
    [InlineData("/api/heroes/abc/counters")]
    [InlineData("/api/nothing-here")]
    public async Task UnmatchedRoute_ReturnsNotFoundProblem(string url)
    {
        await using var factory = new DotaDraftApiFactory();

        var response = await factory.CreateClient().GetAsync(url);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnhandledException_ReturnsServerErrorProblem_WithoutDetailsOutsideDevelopment()
    {
        await using var factory = new DotaDraftApiFactory(environment: "Production", unreachableDatabase: true);

        var response = await GetHeroes(factory);

        await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain(InternalDetail, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnhandledException_IncludesDetailsInDevelopment()
    {
        await using var factory = new DotaDraftApiFactory(unreachableDatabase: true);

        var response = await GetHeroes(factory);

        await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.Contains(InternalDetail, await response.Content.ReadAsStringAsync());
    }
}
