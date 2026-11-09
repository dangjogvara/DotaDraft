using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DotaDraft.Api.Tests.Endpoints;

// Behind App Service the front end terminates HTTPS and forwards plain HTTP, saying what the client used in
// X-Forwarded-Proto. The app has to believe that header, or it would redirect requests that were already HTTPS
public class ForwardedHeadersTests
{
    [Fact]
    public async Task RedirectsToHttps_WhenTheClientUsedHttp()
    {
        await using var factory = CreateFactoryWithHttpsPort();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost/health", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DoesNotRedirect_WhenTheProxySaysTheClientUsedHttps()
    {
        await using var factory = CreateFactoryWithHttpsPort();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The HTTPS redirect only redirects once it knows the HTTPS port, as it would in production with HTTPS set up
    private static WebApplicationFactory<Program> CreateFactoryWithHttpsPort() =>
        new DotaDraftApiFactory(environment: "Production")
            .WithWebHostBuilder(builder => builder.UseSetting("https_port", "443"));
}
