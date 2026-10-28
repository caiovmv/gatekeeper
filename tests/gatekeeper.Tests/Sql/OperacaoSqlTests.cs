using gatekeeper.Audit;
using gatekeeper.Authorization;
using gatekeeper.Billing;
using gatekeeper.Governance;
using gatekeeper.Persistence;
using gatekeeper.Persistence.Adapters;
using Xunit;

namespace gatekeeper.Tests.Sql;

public class OperacaoSqlTests
{
    [Fact]
    public void Sqlite_mantem_membro_uso_auditoria_politica_e_limite_depois_de_reabrir()
    {
        RegistroDeProvedores.Registrar(ProvedoresConhecidos.Sqlite);
        var caminho = Path.Combine(Path.GetTempPath(), "gatekeeper-operacao-" + Guid.NewGuid().ToString("N") + ".db");
        var organizacaoId = Guid.NewGuid();
        var agora = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        try
        {
            var sessao = CriarSessao(caminho);
            var escritorio = new SqlOfficeStore(sessao);
            var membroId = Guid.NewGuid();
            escritorio.Save(new Member(membroId, organizacaoId, "ada@example.com", "Ada", "support"));
            var emitida = escritorio.Issue(Guid.NewGuid(), organizacaoId, "borda", agora);

            var auditoria = new SqlAuditTrail(sessao, new PublicadorNulo());
            auditoria.PublishEntry("staff", "member.create", membroId.ToString("D"), "ok", "corr");
            var politica = new SqlPolicyStore(sessao);
            Assert.True(politica.Allows(new[] { "support" }, "read", "audit"));
            var janela = new SqlWindowStore(sessao);
            var relogio = new SessionClock(janela);
            Assert.False(relogio.Expired("staff", agora));

            var reaberto = CriarSessao(caminho);
            var escritorioReaberto = new SqlOfficeStore(reaberto);
            Assert.Equal("ada@example.com", Assert.Single(escritorioReaberto.Members()).Email);
            Assert.True(escritorioReaberto.Touch(emitida.Secret, agora.AddMinutes(1)));

            var auditoriaReaberta = new SqlAuditTrail(reaberto, new PublicadorNulo());
            Assert.True(auditoriaReaberta.Verify());
            Assert.Equal("member.create", Assert.Single(auditoriaReaberta.Entries).Action);

            var limite = new LoginThrottle(new SqlWindowStore(reaberto));
            for (var i = 0; i < 10; i++)
            {
                Assert.True(limite.Allow("10.0.0.1", "ada@example.com", agora));
            }

            Assert.False(new LoginThrottle(new SqlWindowStore(reaberto)).Allow("10.0.0.1", "ada@example.com", agora.AddSeconds(1)));
            Assert.True(new SessionClock(new SqlWindowStore(reaberto)).Expired("staff", agora.AddMinutes(31)));
        }
        finally
        {
            if (File.Exists(caminho))
            {
                File.Delete(caminho);
            }
        }
    }

    private static SessaoSql CriarSessao(string caminho)
    {
        var opcoes = new OpcoesDeBanco(ProvedoresConhecidos.Sqlite, "Data Source=" + caminho + ";Pooling=False");
        return new SessaoSql(new FabricaDeConexao(opcoes), DialetosSql.Para(opcoes.ProvedorInvariante));
    }

    private sealed class PublicadorNulo : IAuditPublisher
    {
        public void Publish(AuditEntry entry)
        {
        }
    }
}
