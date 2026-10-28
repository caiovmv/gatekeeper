namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// Nomes amigaveis viram o invariant name do ADO.NET. Um nome desconhecido passa intacto:
/// e o jeito de plugar Oracle ou qualquer outro driver ja registrado no processo.
/// </summary>
public static class ProvedoresConhecidos
{
    public const string SqlServer = "Microsoft.Data.SqlClient";

    public const string PostgreSql = "Npgsql";

    public const string MySql = "MySqlConnector";

    public const string Sqlite = "Microsoft.Data.Sqlite";

    public static string Invariante(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
        {
            throw new ArgumentException("Database:Provider e obrigatorio.", nameof(provedor));
        }

        if (Eh(provedor, "SqlServer") || Eh(provedor, SqlServer))
        {
            return SqlServer;
        }

        if (Eh(provedor, "PostgreSQL") || Eh(provedor, "Postgres") || Eh(provedor, PostgreSql))
        {
            return PostgreSql;
        }

        if (Eh(provedor, "MySQL") || Eh(provedor, "MariaDB") || Eh(provedor, MySql))
        {
            return MySql;
        }

        if (Eh(provedor, "SQLite") || Eh(provedor, Sqlite))
        {
            return Sqlite;
        }

        return provedor.Trim();
    }

    private static bool Eh(string valor, string esperado)
    {
        return string.Equals(valor.Trim(), esperado, StringComparison.OrdinalIgnoreCase);
    }
}
