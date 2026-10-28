using gatekeeper.Common;

namespace gatekeeper.Persistence;

/// <summary>
/// Escrita. Consulta que nao cabe aqui ganha uma porta propria, no lugar de alargar este contrato.
/// </summary>
public interface IWriteRepository<TEntidade>
    where TEntidade : Entidade
{
    Task AdicionarAsync(TEntidade entidade, CancellationToken cancellationToken);
}
