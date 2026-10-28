namespace gatekeeper.Api;

/// <summary>
/// Cabecalhos de toda resposta. A API fica fechada. Swagger e ReDoc recebem um CSP que ainda e do proprio host.
/// </summary>
public sealed class CabecalhosDeSeguranca
{
    public const string PoliticaDeApi = "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public const string PoliticaDeDocumento = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; frame-ancestors 'none'";

    private readonly RequestDelegate _proximo;

    public CabecalhosDeSeguranca(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            Aplicar(contexto);
            return Task.CompletedTask;
        });
        await _proximo(contexto);
    }

    private static void Aplicar(HttpContext contexto)
    {
        var cabecalhos = contexto.Response.Headers;
        cabecalhos.Remove("Server");
        cabecalhos["X-Content-Type-Options"] = "nosniff";
        cabecalhos["X-Frame-Options"] = "DENY";
        cabecalhos["Referrer-Policy"] = "no-referrer";
        cabecalhos["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        cabecalhos["X-Permitted-Cross-Domain-Policies"] = "none";
        cabecalhos["X-DNS-Prefetch-Control"] = "off";
        cabecalhos["Cross-Origin-Opener-Policy"] = "same-origin";
        cabecalhos["Cross-Origin-Resource-Policy"] = "same-origin";

        if (EhDocumento(contexto.Request.Path))
        {
            cabecalhos["Content-Security-Policy"] = PoliticaDeDocumento;
        }
        else
        {
            cabecalhos["Cache-Control"] = "no-store";
            cabecalhos["Content-Security-Policy"] = PoliticaDeApi;
        }

        if (contexto.Request.IsHttps)
        {
            cabecalhos["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }
    }

    private static bool EhDocumento(PathString caminho)
    {
        return caminho.StartsWithSegments("/swagger")
            || caminho.StartsWithSegments("/redoc")
            || caminho.StartsWithSegments("/asyncapi");
    }
}

public static class CabecalhosDeSegurancaExtensions
{
    public static WebApplication UseCabecalhosDeSeguranca(this WebApplication app)
    {
        app.UseMiddleware<CabecalhosDeSeguranca>();
        return app;
    }
}
