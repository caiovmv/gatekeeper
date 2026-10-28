using Microsoft.Extensions.Hosting;
using Temporalio.Client;
using Temporalio.Worker;

namespace gatekeeper.Workflows.Adapters;

/// <summary>Sobe o worker na fila configurada. Sem endpoint, este servico nao e registrado.</summary>
public sealed class TrabalhadorTemporal : BackgroundService
{
    private readonly OpcoesDeWorkflow _opcoes;

    public TrabalhadorTemporal(OpcoesDeWorkflow opcoes)
    {
        _opcoes = opcoes;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conexao = new TemporalClientConnectOptions
        {
            TargetHost = _opcoes.Endpoint,
            Namespace = _opcoes.NamespaceDeTrabalho,
        };
        if (_opcoes.Tls)
        {
            conexao.Tls = new TlsOptions();
        }

        var cliente = await TemporalClient.ConnectAsync(conexao);
        var opcoes = new TemporalWorkerOptions(_opcoes.Fila);
        opcoes.AddWorkflow<FluxoDeAtividade>();
        opcoes.AddAllActivities(new AtividadeDeEco());
        using var trabalhador = new TemporalWorker(cliente, opcoes);
        await trabalhador.ExecuteAsync(stoppingToken);
    }
}
