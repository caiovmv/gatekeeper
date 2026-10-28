using System.Data.Common;

namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// Chave duplicada sem referenciar SqlException, PostgresException ou MySqlException.
/// SqlState 23505/23000 cobre Postgres, MySQL e o padrao. Number 2627/2601 cobre o SQL Server
/// quando o driver nao preenche SqlState. SqliteErrorCode 19 cobre o SQLite.
/// </summary>
public static class ConflitoDeChave
{
    public static bool EhViolacao(DbException excecao)
    {
        var estado = excecao.SqlState;
        if (estado == "23505" || estado == "23000")
        {
            return true;
        }

        var numero = excecao.GetType().GetProperty("Number")?.GetValue(excecao);
        if (numero is int valor && (valor == 2627 || valor == 2601 || valor == 1062))
        {
            return true;
        }

        var codigoSqlite = excecao.GetType().GetProperty("SqliteErrorCode")?.GetValue(excecao);
        if (codigoSqlite is not null && Convert.ToInt32(codigoSqlite) == 19)
        {
            return true;
        }

        return false;
    }
}
