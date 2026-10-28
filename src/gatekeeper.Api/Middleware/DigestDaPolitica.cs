using gatekeeper.Governance;

namespace gatekeeper.Api;

public sealed class DigestDaPolitica
{
    private readonly RequestDelegate _proximo;

    private readonly GovernanceLedger _ledger;

    public DigestDaPolitica(RequestDelegate proximo, GovernanceLedger ledger)
    {
        _proximo = proximo;
        _ledger = ledger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (!Mutacao(contexto.Request) || _ledger.ActiveDigestText().Length == 0 || _ledger.DigestMatches(contexto.Request.Headers["X-Policy-Digest"].ToString()))
        {
            await _proximo(contexto);
            return;
        }

        GovernanceMeters.AuthorizationDenied.Add(1);
        contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
        await contexto.Response.WriteAsJsonAsync(new { status = 403, detail = "digest mismatch" });
    }

    private static bool Mutacao(HttpRequest request)
    {
        var method = request.Method;
        if (method == HttpMethods.Get || method == HttpMethods.Head || method == HttpMethods.Options)
        {
            return false;
        }

        return request.Path.StartsWithSegments("/api");
    }
}

public static class DigestDaPoliticaExtensions
{
    public static WebApplication UseDigestDaPolitica(this WebApplication app)
    {
        app.UseMiddleware<DigestDaPolitica>();
        return app;
    }
}
