using System.Net;
using gatekeeper.Governance;

namespace gatekeeper.Api;

public sealed class LimiteDeMutacao
{
    private readonly RequestDelegate _proximo;

    private readonly MutationBudget _budget;

    public LimiteDeMutacao(RequestDelegate proximo, MutationBudget budget)
    {
        _proximo = proximo;
        _budget = budget;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (!Conta(contexto.Request) || Loopback(contexto))
        {
            await _proximo(contexto);
            return;
        }

        var integration = contexto.Request.Path.StartsWithSegments("/api/calls")
            || contexto.Request.Path.StartsWithSegments("/api/account/oidc")
            || contexto.Request.Path.StartsWithSegments("/api/account/saml");
        // As escritas do produto (equipes, processos, convites) são frequentes: têm balde e limite próprios.
        var product = contexto.Request.Path.StartsWithSegments("/api/workspaces") || contexto.Request.Path.StartsWithSegments("/api/scope");
        var limit = integration ? 300 : product ? 600 : 60;
        var bucket = integration ? "integration" : product ? "product" : "admin";
        var key = contexto.Connection.RemoteIpAddress?.ToString() ?? "remote";
        if (_budget.Allow(bucket, key, limit, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow))
        {
            await _proximo(contexto);
            return;
        }

        contexto.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        contexto.Response.Headers.RetryAfter = "60";
        await contexto.Response.WriteAsJsonAsync(new { status = 429, detail = "rate limit" });
    }

    private static bool Conta(HttpRequest request)
    {
        if (request.Method == HttpMethods.Get || request.Method == HttpMethods.Head || request.Method == HttpMethods.Options)
        {
            return false;
        }

        if (request.Path.StartsWithSegments("/health") || request.Path.StartsWithSegments("/api/account/login") || request.Path.StartsWithSegments("/api/account/setup"))
        {
            return false;
        }

        return request.Path.StartsWithSegments("/api");
    }

    private static bool Loopback(HttpContext contexto)
    {
        var address = contexto.Connection.RemoteIpAddress;
        return address is null || IPAddress.IsLoopback(address);
    }
}

public static class LimiteDeMutacaoExtensions
{
    public static WebApplication UseLimiteDeMutacao(this WebApplication app)
    {
        app.UseMiddleware<LimiteDeMutacao>();
        return app;
    }
}
