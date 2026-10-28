using gatekeeper.Persistence.Adapters;
using Xunit;

namespace gatekeeper.Tests.Sql;

public class DialetoSqlTests
{
    [Theory]
    [InlineData("SqlServer", ProvedoresConhecidos.SqlServer)]
    [InlineData("PostgreSQL", ProvedoresConhecidos.PostgreSql)]
    [InlineData("Postgres", ProvedoresConhecidos.PostgreSql)]
    [InlineData("MySQL", ProvedoresConhecidos.MySql)]
    [InlineData("MariaDB", ProvedoresConhecidos.MySql)]
    [InlineData("SQLite", ProvedoresConhecidos.Sqlite)]
    [InlineData("Oracle.ManagedDataAccess.Client", "Oracle.ManagedDataAccess.Client")]
    public void Nome_amigavel_vira_invariant_name(string provedor, string invariante)
    {
        Assert.Equal(invariante, ProvedoresConhecidos.Invariante(provedor));
    }

    [Fact]
    public void SqlServer_pagina_com_offset_fetch_e_texto_unicode()
    {
        var dialeto = DialetosSql.Para(ProvedoresConhecidos.SqlServer);

        Assert.Contains("OFFSET @deslocamento ROWS FETCH NEXT @tamanho ROWS ONLY", dialeto.Paginacao, StringComparison.Ordinal);
        Assert.Contains("nvarchar", dialeto.SqlGarantirTabela, StringComparison.Ordinal);
    }

    [Fact]
    public void Postgres_usa_o_dialeto_ansi()
    {
        var dialeto = DialetosSql.Para(ProvedoresConhecidos.PostgreSql);

        Assert.Contains("CREATE TABLE IF NOT EXISTS", dialeto.SqlGarantirTabela, StringComparison.Ordinal);
        Assert.Contains("OFFSET @deslocamento ROWS FETCH NEXT @tamanho ROWS ONLY", dialeto.Paginacao, StringComparison.Ordinal);
    }

    [Fact]
    public void MySql_pagina_com_limit_e_utf8()
    {
        var dialeto = DialetosSql.Para(ProvedoresConhecidos.MySql);

        Assert.Contains("LIMIT @tamanho OFFSET @deslocamento", dialeto.Paginacao, StringComparison.Ordinal);
        Assert.Contains("utf8mb4", dialeto.SqlGarantirTabela, StringComparison.Ordinal);
    }

    [Fact]
    public void Provedor_desconhecido_cai_no_ansi_sem_mudar_o_repositorio()
    {
        var dialeto = DialetosSql.Para("Oracle.ManagedDataAccess.Client");

        Assert.Contains("CREATE TABLE IF NOT EXISTS", dialeto.SqlGarantirTabela, StringComparison.Ordinal);
        Assert.Contains("OFFSET @deslocamento", dialeto.Paginacao, StringComparison.Ordinal);
    }
}
