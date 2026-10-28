using System.Diagnostics;
using gatekeeper.Observability;

namespace gatekeeper.Api;

public sealed class Correlacao
{
    public const string Cabecalho = "X-Correlacao";

    private static readonly ActivitySource Fonte = new(TelemetryCatalog.Fonte);

    private readonly RequestDelegate _proximo;
    private readonly ILogger<Correlacao> _logger;

    public Correlacao(RequestDelegate proximo, ILogger<Correlacao> logger)
    {
        _proximo = proximo;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        var recebido = contexto.Request.Headers[Cabecalho].ToString();
        if (!string.IsNullOrWhiteSpace(recebido))
        {
            contexto.TraceIdentifier = recebido.Trim();
        }

        Activity? criado = null;
        if (Activity.Current is null)
        {
            criado = Fonte.StartActivity("http.pedido", ActivityKind.Server);
        }

        Activity.Current?.SetTag("correlacao", contexto.TraceIdentifier);
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers[Cabecalho] = contexto.TraceIdentifier;
            return Task.CompletedTask;
        });
        using (_logger.BeginScope(new Dictionary<string, object> { ["Correlacao"] = contexto.TraceIdentifier }))
        {
            try
            {
                await _proximo(contexto);
            }
            finally
            {
                criado?.Dispose();
            }
        }
    }
}

public static class CorrelacaoExtensions
{
    public static WebApplication UseCorrelacao(this WebApplication app)
    {
        app.UseMiddleware<Correlacao>();
        return app;
    }
}
