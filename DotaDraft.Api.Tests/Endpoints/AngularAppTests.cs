using System.Net;

namespace DotaDraft.Api.Tests.Endpoints;

// The API serving a published Angular app from wwwroot. A temp folder stands in for the real build output
public class AngularAppTests : IAsyncLifetime
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("dotadraft-wwwroot-").FullName;
    private DotaDraftApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<app-root>the DotaDraft page</app-root>");
        File.WriteAllText(Path.Combine(_webRoot, "main-ABC123.js"), "console.log('the app');");
        _factory = new DotaDraftApiFactory(webRoot: _webRoot);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_webRoot, recursive: true);
    }

    private Task<HttpResponseMessage> Get(string url) => _factory.CreateClient().GetAsync(url);

    [Theory]
    [InlineData("/")]
    [InlineData("/?against=1,14&team=82")]
    [InlineData("/some/deep/link")]
    public async Task AppUrls_GetThePage(string url)
    {
        var response = await Get(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("the DotaDraft page", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BuiltFiles_AreServedAsTheyAre()
    {
        var response = await Get("/main-ABC123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("console.log('the app');", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingFiles_AreNotFound_NotThePage()
    {
        var response = await Get("/main-MISSING.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/api/heroes/1/counters")]
    public async Task UnknownApiUrls_StayNotFoundProblems(string url)
    {
        var response = await Get(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WrongMethodOnAnApiUrl_StaysMethodNotAllowed()
    {
        var response = await _factory.CreateClient().PostAsync("/api/counters?against=1", null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/heroes", "application/json")]
    [InlineData("/health", "text/plain")]
    public async Task ApiAndHealth_StillAnswer(string url, string mediaType)
    {
        var response = await Get(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }
}
