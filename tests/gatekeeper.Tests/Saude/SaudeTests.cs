using System.Net;
using gatekeeper.Persistence.Adapters;
using gatekeeper.Api;
using gatekeeper.Health;
using gatekeeper.Messaging;
using gatekeeper.Observability;
using gatekeeper.Workflows;
using gatekeeper.Messaging.Adapters;
using gatekeeper.Health.Adapters;
using gatekeeper.Workflows.Adapters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace gatekeeper.Tests.Health;

public class SaudeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabrica;

    public SaudeTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
    }

    [Fact]
    public async Task Health_responde_so_o_processo()
    {
        var cliente = _fabrica.CreateClient();
        var resposta = await cliente.GetAsync("/health");
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Contains("processo", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("mensagens-interno", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("workflows", corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_complete_lista_os_componentes_em_memoria()
    {
        var cliente = _fabrica.CreateClient();
        var resposta = await cliente.GetAsync("/health/complete");
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        foreach (var nome in NomesDeSonda.Todos)
        {
            Assert.Contains(nome, corpo, StringComparison.Ordinal);
        }

        Assert.Contains("memoria", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("processo", corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Banco_sqlite_responde_select_1()
    {
        var caminho = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
        RegistroDeProvedores.Registrar(ProvedoresConhecidos.Sqlite);
        var fabrica = new FabricaDeConexao(new OpcoesDeBanco(ProvedoresConhecidos.Sqlite, "Data Source=" + caminho + ";Pooling=False"));
        var sonda = new SondaDeBanco(fabrica);

        try
        {
            var estado = await sonda.VerificarAsync(CancellationToken.None);
            Assert.True(estado.Saudavel);
            Assert.Equal("ok", estado.Detalhe);
        }
        finally
        {
            if (File.Exists(caminho))
            {
                File.Delete(caminho);
            }
        }
    }

    [Fact]
    public async Task Adaptador_marca_unhealthy_sem_vazar_credencial()
    {
        var estado = EstadoDaSonda.Falha(new InvalidOperationException("Authorization: Bearer segredo"));
        Assert.False(estado.Saudavel);
        Assert.DoesNotContain("segredo", estado.Detalhe, StringComparison.Ordinal);

        var adaptador = new AdaptadorDeSonda(new SondaQueFalha(), new TelemetriaDeServico());
        var resultado = await adaptador.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        Assert.Equal(HealthStatus.Unhealthy, resultado.Status);
        Assert.Equal("falhou", resultado.Description);
    }

    [Fact]
    public async Task Configuracao_vazia_registra_fila_e_workflow_em_memoria()
    {
        var services = new ServiceCollection();
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddEsqueleto(configuracao);
        using var provedor = services.BuildServiceProvider();

        var interno = provedor.GetRequiredKeyedService<IMessageBroker>(MessageRoles.Interno);
        var externo = provedor.GetRequiredKeyedService<IMessageBroker>(MessageRoles.Externo);
        var motor = provedor.GetRequiredService<IWorkflowEngine>();
        await interno.PublicarAsync(new QueueMessage("eco", "oi"), CancellationToken.None);
        var execucao = await motor.IniciarAsync(new WorkflowRequest("eco", "id-1", "oi"), CancellationToken.None);

        Assert.IsType<InMemoryMessageBroker>(interno);
        Assert.IsType<InMemoryMessageBroker>(externo);
        Assert.NotSame(interno, externo);
        Assert.Equal("concluido", execucao.Estado);
    }

    private sealed class SondaQueFalha : ISondaDeInfraestrutura
    {
        public string Nome => "banco";

        public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new EstadoDaSonda(false, "falhou"));
        }
    }

    private sealed class Fila : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _responder;

        public Fila(Func<string, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<string> Pedidos { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var texto = request.Method + " " + request.RequestUri;
            Pedidos.Add(texto);
            return Task.FromResult(_responder(texto));
        }
    }
}
