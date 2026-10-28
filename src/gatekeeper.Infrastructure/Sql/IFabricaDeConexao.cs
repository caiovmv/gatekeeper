using System.Data.Common;

namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// Abre DbConnection. Nao sabe se o driver e SQL Server, PostgreSQL, MySQL ou outro.
/// </summary>
public interface IFabricaDeConexao
{
    DbConnection Criar();
}
