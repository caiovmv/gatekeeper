using gatekeeper.Billing;
using gatekeeper.Persistence;
using gatekeeper.Persistence.Adapters;
using Xunit;

namespace gatekeeper.Tests.Sql;

public class EscritorioSqlTests
{
    [Fact]
    public void Sqlite_mantem_organizacao_depois_de_reabrir()
    {
        RegistroDeProvedores.Registrar(ProvedoresConhecidos.Sqlite);
        var caminho = Path.Combine(Path.GetTempPath(), "gatekeeper-office-" + Guid.NewGuid().ToString("N") + ".db");
        var organizacaoId = Guid.NewGuid();

        try
        {
            var loja = Criar(caminho);
            loja.Save(new Organization(organizacaoId, "Live", organizacaoId.ToString("N") + ".test", "", ""));
            loja.Save(new Organization(organizacaoId, "Live Plus", organizacaoId.ToString("N") + ".test", "https://idp.example", ""));
            loja.RemoveOrganization(Guid.NewGuid());

            var reaberta = Criar(caminho);
            var organizacao = Assert.Single(reaberta.Organizations());
            Assert.Equal("Live Plus", organizacao.Name);
            Assert.Equal("https://idp.example", organizacao.IdpIssuer);

            reaberta.RemoveOrganization(organizacaoId);
            Assert.Empty(Criar(caminho).Organizations());
        }
        finally
        {
            if (File.Exists(caminho))
            {
                File.Delete(caminho);
            }
        }
    }

    private static SqlBillingStore Criar(string caminho)
    {
        var opcoes = new OpcoesDeBanco(ProvedoresConhecidos.Sqlite, "Data Source=" + caminho + ";Pooling=False");
        var sessao = new SessaoSql(new FabricaDeConexao(opcoes), DialetosSql.Para(opcoes.ProvedorInvariante));
        return new SqlBillingStore(sessao);
    }
}
