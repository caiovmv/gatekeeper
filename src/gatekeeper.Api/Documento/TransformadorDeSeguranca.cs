using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace gatekeeper.Api;

/// <summary>
/// Publica os dois esquemas que a borda aceita: bearer do IdP e a chave de API.
/// </summary>
public sealed class TransformadorDeSeguranca : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = "gatekeeper";
        document.Info.Version = "v1";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Name = "Authorization",
        };
        document.Components.SecuritySchemes["apiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
        };
        return Task.CompletedTask;
    }
}
