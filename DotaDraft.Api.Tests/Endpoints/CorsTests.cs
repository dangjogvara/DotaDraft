using Microsoft.AspNetCore.Hosting;

namespace DotaDraft.Api.Tests.Endpoints;

public class CorsTests : IAsyncLifetime
{
    private const string AllowedOrigin = "http://allowed.test";

    private DotaDraftApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = await DotaDraftApiFactory.CreateAsync();

        // Index 0 replaces the dev default (localhost:4200), so the test doesn't depend on appsettings
        _client = _factory
            .WithWebHostBuilder(b => b.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin))
            .CreateClient();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static HttpRequestMessage Preflight(string origin, string method, string? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/heroes");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        if (headers is not null)
            request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    [Fact]
    public async Task AllowsConfiguredOrigin_OnGet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/heroes");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OmitsCorsHeaders_ForOtherOrigins()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/heroes");
        request.Headers.Add("Origin", "http://evil.test");

        var response = await _client.SendAsync(request);

        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_AllowsGet()
    {
        var response = await _client.SendAsync(Preflight(AllowedOrigin, "GET"));

        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("GET", Header(response, "Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task Preflight_RejectsOtherOrigins_AndUnlistedMethods()
    {
        var otherOrigin = await _client.SendAsync(Preflight("http://evil.test", "GET"));
        var postMethod = await _client.SendAsync(Preflight(AllowedOrigin, "POST"));

        Assert.Null(Header(otherOrigin, "Access-Control-Allow-Origin"));
        // ASP.NET Core answers with the allowed list rather than refusing; the browser then blocks POST
        Assert.DoesNotContain("POST", Header(postMethod, "Access-Control-Allow-Methods"));
    }
}
