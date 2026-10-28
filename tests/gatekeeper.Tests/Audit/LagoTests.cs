using System.Text.Json;
using gatekeeper.Audit;
using gatekeeper.Audit.Adapters;
using gatekeeper.Files.Adapters;
using Xunit;

namespace gatekeeper.Tests.Audit;

public class LagoTests
{
    [Fact]
    public async Task Auditoria_grava_json_e_a_trilha_continua_depois_de_reabrir()
    {
        var arquivos = new InMemoryFileStore();
        var trilha = new TrilhaNoLago(arquivos, new PublicadorNulo());
        var primeira = trilha.PublishEntry("ada", "organization.create", "org-1", "allow", "corr-1");
        var segunda = trilha.PublishEntry("ada", "member.create", "org-1", "allow", "corr-2");

        Assert.Equal(string.Empty, primeira.PreviousHash);
        Assert.Equal(primeira.Hash, segunda.PreviousHash);
        Assert.True(trilha.Verify());

        var reaberta = new TrilhaNoLago(arquivos, new PublicadorNulo());
        var terceira = reaberta.PublishEntry("ada", "policy.save", "org-1", "allow", "corr-3");
        Assert.Equal(segunda.Hash, terceira.PreviousHash);
        Assert.True(reaberta.Verify());
        Assert.Equal(3, reaberta.Entries.Count);

        var chaves = await arquivos.ListarAsync(TrilhaNoLago.Prefixo, CancellationToken.None);
        Assert.Contains(chaves, chave => chave.Contains("/dt=", StringComparison.Ordinal) && chave.EndsWith(primeira.Hash + ".json", StringComparison.Ordinal));
        using var corpo = await arquivos.AbrirAsync(chaves.First(chave => chave.EndsWith(primeira.Hash + ".json", StringComparison.Ordinal)), CancellationToken.None);
        Assert.NotNull(corpo);
        using var documento = JsonDocument.Parse(corpo);
        Assert.Equal("organization.create", documento.RootElement.GetProperty("action").GetString());
        Assert.Equal("allow", documento.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Log_sai_como_json_sem_consulta_na_url()
    {
        var arquivos = new InMemoryFileStore();
        var logs = new LogsNoLago(arquivos, new PublicadorDeLogNulo());
        logs.Append(new AccessLog(DateTimeOffset.Parse("2026-10-28T12:00:00Z"), "corr", "ada", Guid.NewGuid(), "get", "/api/members?token=segredo", 200, 12));

        var gravado = logs.Entries.Single();
        Assert.Equal("/api/members", gravado.Path);
        Assert.Equal("GET", gravado.Method);

        var chaves = await arquivos.ListarAsync(LogsNoLago.Prefixo, CancellationToken.None);
        var chave = Assert.Single(chaves);
        Assert.Contains("/dt=2026-10-28/", chave, StringComparison.Ordinal);
        Assert.EndsWith(".json", chave, StringComparison.Ordinal);
    }

    private sealed class PublicadorNulo : IAuditPublisher
    {
        public void Publish(AuditEntry entry)
        {
        }
    }
}
