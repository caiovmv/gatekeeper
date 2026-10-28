using gatekeeper.Common;
using gatekeeper.Persistence;
using Microsoft.Extensions.Logging;

namespace gatekeeper.Persistence;

/// <summary>
/// Registra a operacao, nao o payload. Texto de pedido pode carregar dado de cliente.
/// Falha esperada vira aviso. Excecao sobe depois de registrada.
/// </summary>
public sealed class LoggingBehavior<TPedido, TResposta> : IBehavior<TPedido, TResposta>
{
    private readonly ILogger<LoggingBehavior<TPedido, TResposta>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TPedido, TResposta>> logger)
    {
        _logger = logger;
    }

    public async Task<Resultado<TResposta>> ExecutarAsync(
        TPedido pedido,
        Func<CancellationToken, Task<Resultado<TResposta>>> proximo,
        CancellationToken cancellationToken)
    {
        var operacao = typeof(TPedido).Name;
        _logger.LogInformation("Operacao {Operacao} iniciada.", operacao);

        try
        {
            var resultado = await proximo(cancellationToken);
            if (resultado.Sucesso)
            {
                _logger.LogInformation("Operacao {Operacao} concluida.", operacao);
                return resultado;
            }

            _logger.LogWarning(
                "Operacao {Operacao} recusada. Codigo {Codigo}.",
                operacao,
                resultado.Erro.Codigo);
            return resultado;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExcecaoDeDominio excecao)
        {
            _logger.LogWarning(
                "Operacao {Operacao} recusada. Codigo {Codigo}.",
                operacao,
                excecao.Erro.Codigo);
            throw;
        }
        catch (Exception excecao)
        {
            _logger.LogError(excecao, "Operacao {Operacao} falhou.", operacao);
            throw;
        }
    }
}
