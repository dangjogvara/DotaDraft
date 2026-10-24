using DotaDraft.Api.Authentication;
using DotaDraft.Api.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Authentication;

public class AdminKeyAuthenticationHandlerTests
{
    private const string Key = "the-admin-key";

    private static async Task<AuthenticateResult> Authenticate(string? configuredKey, string? header)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AdminEndpoints.AdminKeyConfigKey] = configuredKey })
            .Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(config)
            .AddAdminKeyAuthentication()
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        if (header is not null)
            context.Request.Headers[AdminKeyDefaults.HeaderName] = header;

        return await context.AuthenticateAsync(AdminKeyDefaults.SchemeName);
    }

    [Fact]
    public async Task CorrectKey_AuthenticatesAsAdmin()
    {
        var result = await Authenticate(Key, Key);

        Assert.True(result.Succeeded);
        Assert.Equal(AdminKeyDefaults.SchemeName, result.Principal!.Identity!.AuthenticationType);
    }

    [Fact]
    public async Task NoHeader_IsNoResult()
    {
        var result = await Authenticate(Key, header: null);

        Assert.True(result.None);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("THE-ADMIN-KEY")]
    [InlineData("the-admin-key ")]
    [InlineData("")]
    public async Task WrongKey_Fails(string header)
    {
        var result = await Authenticate(Key, header);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UnsetKey_FailsEvenForEmptyHeader(string? configuredKey)
    {
        var result = await Authenticate(configuredKey, "");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }
}
