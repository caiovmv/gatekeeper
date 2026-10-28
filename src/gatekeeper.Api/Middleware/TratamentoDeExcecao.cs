using gatekeeper.Api;
using gatekeeper.Identity;
using gatekeeper.Common;

namespace gatekeeper.Api;

/// <summary>
/// Ultima rede. Regra de dominio vira o codigo do erro. Falha inesperada vira mensagem fixa, sem a excecao.
/// </summary>
public sealed class TratamentoDeExcecao
{
    private readonly RequestDelegate _proximo;
    private readonly ILogger<TratamentoDeExcecao> _logger;

    public TratamentoDeExcecao(RequestDelegate proximo, ILogger<TratamentoDeExcecao> logger)
    {
        _proximo = proximo;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await _proximo(contexto);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExcecaoDeDominio excecao) when (!contexto.Response.HasStarted)
        {
            _logger.LogWarning(
                "Regra de dominio recusou {Metodo} {Caminho}. Codigo {Codigo}. Rastro {Rastro}.",
                contexto.Request.Method,
                contexto.Request.Path.Value,
                excecao.Erro.Codigo,
                contexto.TraceIdentifier);
            await EscreverAsync(contexto, excecao.Erro);
        }
        catch (Exception excecao) when (!contexto.Response.HasStarted)
        {
            _logger.LogError(
                "Falha nao tratada em {Metodo} {Caminho}. Rastro {Rastro}. Detalhe {Detalhe}.",
                contexto.Request.Method,
                contexto.Request.Path.Value,
                contexto.TraceIdentifier,
                RedacaoDeSegredo.Aplicar(excecao.Message));
            await EscreverAsync(contexto, Erro.Inesperado());
        }
    }

    private static Task EscreverAsync(HttpContext contexto, Erro erro)
    {
        var corpo = Results.Json(RespostaHttp.ParaProblema(erro), statusCode: RespostaHttp.StatusDe(erro));
        return corpo.ExecuteAsync(contexto);
    }
}

public static class TratamentoDeExcecaoExtensions
{
    public static WebApplication UseTratamentoDeExcecao(this WebApplication app)
    {
        app.UseMiddleware<TratamentoDeExcecao>();
        return app;
    }
}
