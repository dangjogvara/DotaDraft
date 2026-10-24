using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Authentication;

public class AdminKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The key callers must send in the X-Admin-Key header. Nobody is authenticated while it's unset.</summary>
    public string? ApiKey { get; set; }
}

// Authenticates a caller as the admin when the X-Admin-Key header matches Admin:ApiKey
public class AdminKeyAuthenticationHandler(
    IOptionsMonitor<AdminKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AdminKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AdminKeyDefaults.HeaderName, out var provided))
            return Task.FromResult(AuthenticateResult.NoResult());

        // Fail closed if no key is configured, so an empty header can never match
        if (string.IsNullOrEmpty(Options.ApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Admin:ApiKey is not configured."));

        // Fixed-time comparison, so response timing doesn't reveal how much of the key matched
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided.ToString()), Encoding.UTF8.GetBytes(Options.ApiKey)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid admin key."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
