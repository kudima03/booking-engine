using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BookingEngine.Api;

/// <summary>
/// Declares the bearer scheme on the OpenAPI document so API explorers offer a token field.
/// </summary>
public sealed record BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            Description = "Access token returned by POST /auth/login.",
        };

        return Task.CompletedTask;
    }
}
