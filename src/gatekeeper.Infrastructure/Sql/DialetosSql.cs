namespace gatekeeper.Persistence.Adapters;

public static class DialetosSql
{
    public static IDialetoSql Para(string provedorInvariante)
    {
        if (provedorInvariante == ProvedoresConhecidos.SqlServer)
        {
            return DialetoSqlServer.Instancia;
        }

        if (provedorInvariante == ProvedoresConhecidos.MySql)
        {
            return DialetoMySql.Instancia;
        }

        if (provedorInvariante == ProvedoresConhecidos.Sqlite)
        {
            return DialetoSqlite.Instancia;
        }

        return DialetoAnsi.Instancia;
    }
}
