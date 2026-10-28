using gatekeeper.Persistence;
using gatekeeper.Common;

namespace gatekeeper.Persistence;

/// <summary>
/// Encadeia os comportamentos e, no fim, o servico. O caso de uso nao sabe que existe log.
/// </summary>
public sealed class ServiceChain<TPedido, TResposta> : IApplicationService<TPedido, TResposta>
{
    private readonly IApplicationService<TPedido, TResposta> _servico;
    private readonly IReadOnlyList<IBehavior<TPedido, TResposta>> _comportamentos;

    public ServiceChain(
        IApplicationService<TPedido, TResposta> servico,
        IReadOnlyList<IBehavior<TPedido, TResposta>> comportamentos)
    {
        _servico = servico;
        _comportamentos = comportamentos;
    }

    public async Task<Resultado<TResposta>> ExecutarAsync(TPedido pedido, CancellationToken cancellationToken)
    {
        var indice = 0;

        Task<Resultado<TResposta>> Proximo(CancellationToken token)
        {
            if (indice >= _comportamentos.Count)
            {
                return _servico.ExecutarAsync(pedido, token);
            }

            var comportamento = _comportamentos[indice];
            indice++;
            return comportamento.ExecutarAsync(pedido, Proximo, token);
        }

        return await Proximo(cancellationToken);
    }
}
