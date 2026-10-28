using gatekeeper.Common;

namespace gatekeeper.Persistence;

/// <summary>
/// Um servico, um pedido, uma resposta. Controller e rota chamam isto, nunca o repositorio.
/// </summary>
public interface IApplicationService<TPedido, TResposta>
{
    Task<Resultado<TResposta>> ExecutarAsync(TPedido pedido, CancellationToken cancellationToken);
}
