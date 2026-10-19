using System.Net;
using System.Net.Http.Json;
using DotaDraft.Api.Endpoints;
using Microsoft.AspNetCore.Mvc;

namespace DotaDraft.Api.Tests.Endpoints;

// Every error, whether returned explicitly, an empty status code or an unhandled exception, is ProblemDetails
public class ErrorResponseTests
{
    private const string BugMessage = "Simulated bug with internal details";

    private static async Task<ProblemDetails> AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)status, problem!.Status);
        return problem;
    }

    private static Task<HttpResponseMessage> SeedHeroes(DotaDraftApiFactory factory)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/seed-heroes");
        request.Headers.Add(AdminKeyFilter.HeaderName, DotaDraftApiFactory.AdminKey);
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
    public async Task MissingAdminKey_ReturnsUnauthorizedProblem()
    {
        await using var factory = new DotaDraftApiFactory();

        var response = await factory.CreateClient().PostAsync("/api/admin/seed-heroes", null);

        await AssertProblem(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnhandledException_ReturnsServerErrorProblem_WithoutDetailsOutsideDevelopment()
    {
        await using var factory = new DotaDraftApiFactory(environment: "Production");
        factory.OpenDota.HeroStatsBug = new InvalidOperationException(BugMessage);

        var response = await SeedHeroes(factory);

        await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain(BugMessage, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnhandledException_IncludesDetailsInDevelopment()
    {
        await using var factory = new DotaDraftApiFactory();
        factory.OpenDota.HeroStatsBug = new InvalidOperationException(BugMessage);

        var response = await SeedHeroes(factory);

        await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.Contains(BugMessage, await response.Content.ReadAsStringAsync());
    }
}
