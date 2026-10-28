using gatekeeper.Common;

namespace gatekeeper.Persistence;

/// <summary>
/// Leitura. Quem so consulta nao recebe metodo de gravacao.
/// </summary>
public interface IReadRepository<TEntidade>
    where TEntidade : Entidade
{
    Task<TEntidade?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Pagina<TEntidade>> ListarAsync(int pagina, int tamanho, CancellationToken cancellationToken);
}
