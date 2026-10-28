using gatekeeper.Workflows;
using gatekeeper.Workflows.Adapters;
using Xunit;

namespace gatekeeper.Tests.Workflows;

public class WorkflowsTests
{
    [Fact]
    public async Task Memoria_conclui_e_recusa_id_repetido()
    {
        var motor = new InMemoryWorkflowEngine();
        var execucao = await motor.IniciarAsync(new WorkflowRequest("eco", "id-1", "oi"), CancellationToken.None);

        Assert.Equal("id-1", execucao.Id);
        Assert.Equal("concluido", execucao.Estado);
        Assert.Equal("eco:oi", execucao.Resultado);
        Assert.Equal("eco", motor.Obter("id-1")!.Nome);
        await motor.SinalizarAsync("id-1", "aviso", "pare", CancellationToken.None);
        var consultada = await motor.ConsultarAsync("id-1", CancellationToken.None);
        Assert.Equal("aviso:pare", consultada!.Sinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => motor.CancelarAsync("id-1", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => motor.IniciarAsync(new WorkflowRequest("eco", "id-1", "de novo"), CancellationToken.None));
    }

    [Fact]
    public void Https_liga_tls_e_tira_o_esquema()
    {
        var opcoes = DestinoDeWorkflow.Montar("https://temporal.interno:7233", null, null);
        Assert.Equal("temporal.interno:7233", opcoes.Endpoint);
        Assert.True(opcoes.Tls);
        Assert.Equal("default", opcoes.NamespaceDeTrabalho);
        Assert.Equal("gatekeeper", opcoes.Fila);

        var local = DestinoDeWorkflow.Montar("localhost:7233", "gatekeeper", "fila");
        Assert.False(local.Tls);
        Assert.Equal("fila", local.Fila);
        Assert.Throws<ArgumentException>(() => DestinoDeWorkflow.Montar("localhost:7233", "nome invalido", null));
    }

    [Fact]
    public void Temporal_nao_conecta_ao_ser_criado()
    {
        var opcoes = DestinoDeWorkflow.Montar("127.0.0.1:1", "default", "gatekeeper");
        var motor = new TemporalWorkflowEngine(opcoes);
        Assert.Equal("workflows", motor.Nome);
        motor.Dispose();
    }
}
