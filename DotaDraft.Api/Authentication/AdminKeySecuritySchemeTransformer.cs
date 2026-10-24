using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DotaDraft.Api.Authentication;

// Describes the X-Admin-Key header in the OpenAPI document and marks the operations that require it,
// so API reference UIs can send the key
public class AdminKeySecuritySchemeTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[AdminKeyDefaults.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = AdminKeyDefaults.HeaderName,
            Description = "The Admin:ApiKey setting."
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var requiresAdmin = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any(a => a.Policy == AdminKeyDefaults.PolicyName);

        if (requiresAdmin)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AdminKeyDefaults.SchemeName, context.Document)] = []
            });
        }
        return Task.CompletedTask;
    }
}
