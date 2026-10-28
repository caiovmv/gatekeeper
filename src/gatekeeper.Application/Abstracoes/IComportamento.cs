using gatekeeper.Common;

namespace gatekeeper.Persistence;

/// <summary>
/// Passo em volta do servico. Log hoje; validacao ou transacao entram como outro comportamento.
/// Chama o proximo uma vez. Nao engole excecao.
/// </summary>
public interface IBehavior<TPedido, TResposta>
{
    Task<Resultado<TResposta>> ExecutarAsync(
        TPedido pedido,
        Func<CancellationToken, Task<Resultado<TResposta>>> proximo,
        CancellationToken cancellationToken);
}
