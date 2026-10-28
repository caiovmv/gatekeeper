using System.Data.Common;
using gatekeeper.Persistence.Adapters;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// Os drivers moram no host. O repositorio so enxerga DbConnection.
/// Provedor fora desta lista precisa ser registrado antes, com o invariant name na config.
/// </summary>
public static class RegistroDeProvedores
{
    public static void Registrar(string invariante)
    {
        if (invariante == ProvedoresConhecidos.SqlServer)
        {
            RegistrarUmaVez(invariante, Microsoft.Data.SqlClient.SqlClientFactory.Instance);
            return;
        }

        if (invariante == ProvedoresConhecidos.PostgreSql)
        {
            RegistrarUmaVez(invariante, NpgsqlFactory.Instance);
            return;
        }

        if (invariante == ProvedoresConhecidos.MySql)
        {
            RegistrarUmaVez(invariante, MySqlConnectorFactory.Instance);
            return;
        }

        if (invariante == ProvedoresConhecidos.Sqlite)
        {
            RegistrarUmaVez(invariante, SqliteFactory.Instance);
        }
    }

    private static void RegistrarUmaVez(string invariante, DbProviderFactory fabrica)
    {
        try
        {
            DbProviderFactories.RegisterFactory(invariante, fabrica);
        }
        catch (ArgumentException)
        {
            // O processo ja registrou este driver.
        }
    }
}
