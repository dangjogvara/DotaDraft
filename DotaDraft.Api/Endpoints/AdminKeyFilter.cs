using System.Security.Cryptography;
using System.Text;

namespace DotaDraft.Api.Endpoints;

// Guards the admin endpoints with a shared key sent in the X-Admin-Key header
public class AdminKeyFilter(string adminKey) : IEndpointFilter
{
    public const string HeaderName = "X-Admin-Key";

    private readonly byte[] _expected = Encoding.UTF8.GetBytes(adminKey);

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        // Fixed-time comparison, so response timing doesn't reveal how much of the key matched
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expected))
            return ValueTask.FromResult<object?>(TypedResults.Unauthorized());

        return next(context);
    }
}
