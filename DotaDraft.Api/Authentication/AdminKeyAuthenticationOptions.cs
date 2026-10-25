using Microsoft.AspNetCore.Authentication;

namespace DotaDraft.Api.Authentication;

public class AdminKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The key callers must send in the X-Admin-Key header. Nobody is authenticated while it's unset.</summary>
    public string? ApiKey { get; set; }
}
