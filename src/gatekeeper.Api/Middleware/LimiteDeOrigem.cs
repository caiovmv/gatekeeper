using gatekeeper.Identity;
using gatekeeper.Observability;
using gatekeeper.Common;

namespace gatekeeper.Api;

public sealed class LimiteDeOrigem
{
    private readonly RequestDelegate _proximo;
    private readonly LimitadorDeOrigem _limitador;

    public LimiteDeOrigem(RequestDelegate proximo, LimitadorDeOrigem limitador)
    {
        _proximo = proximo;
        _limitador = limitador;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (contexto.Request.Path.StartsWithSegments("/health"))
        {
            await _proximo(contexto);
            return;
        }

        var origem = contexto.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(origem))
        {
            origem = "local";
        }

        if (!_limitador.Tentar(origem).Sucesso)
        {
            contexto.RequestServices.GetRequiredService<ITelemetry>().Rejeicao(CodigosDeErro.Conflito);
            contexto.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        await _proximo(contexto);
    }
}

public static class LimiteDeOrigemExtensions
{
    public static WebApplication UseLimiteDeOrigem(this WebApplication app)
    {
        app.UseMiddleware<LimiteDeOrigem>();
        return app;
    }
}