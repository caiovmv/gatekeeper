using gatekeeper.Common;

namespace gatekeeper.Persistence;

public interface IRepository<TEntidade> : IReadRepository<TEntidade>, IWriteRepository<TEntidade>
    where TEntidade : Entidade
{
}
