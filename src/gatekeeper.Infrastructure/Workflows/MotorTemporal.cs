using gatekeeper.Health;
using gatekeeper.Workflows;
using gatekeeper.Health.Adapters;
using Temporalio.Client;

namespace gatekeeper.Workflows.Adapters;

/// <summary>
/// Cliente Temporal. A Api inicia a execucao na fila; o worker que executa as atividades usa o mesmo namespace.
/// </summary>
public sealed class TemporalWorkflowEngine : IWorkflowEngine, ISondaDeInfraestrutura, IDisposable
{
    private readonly Canal _canal = new();
    private readonly OpcoesDeWorkflow _opcoes;

    public TemporalWorkflowEngine(OpcoesDeWorkflow opcoes)
    {
        _opcoes = opcoes;
    }

    public string Nome => NomesDeSonda.Workflows;

    public async Task<WorkflowRun> IniciarAsync(WorkflowRequest pedido, CancellationToken cancellationToken)
    {
        var cliente = await _canal.ObterAsync(_opcoes, cancellationToken);
        var id = string.IsNullOrWhiteSpace(pedido.Id) ? Guid.NewGuid().ToString("N") : pedido.Id.Trim();
            var handle = await cliente.StartWorkflowAsync(
                pedido.Nome,
                new object[] { pedido.Entrada },
                new WorkflowOptions(id: id, taskQueue: _opcoes.Fila));
        return new WorkflowRun(handle.Id, pedido.Nome, "iniciado");
    }

    public async Task<WorkflowRun?> ConsultarAsync(string id, CancellationToken cancellationToken)
    {
        var cliente = await _canal.ObterAsync(_opcoes, cancellationToken);
        var handle = cliente.GetWorkflowHandle(id);
        try
        {
            var descricao = await handle.DescribeAsync();
            var estado = EstadoDe(descricao.Status.ToString());
            string? resultado = null;
            string? sinal = null;
            if (estado == "concluido")
            {
                resultado = await handle.GetResultAsync<string>();
            }

            if (estado == "iniciado")
            {
                sinal = await handle.QueryAsync<string>("sinal", Array.Empty<object?>());
            }

            return new WorkflowRun(id, descricao.WorkflowType, estado, resultado, sinal);
        }
        catch (Exception excecao) when (excecao.GetType().Name.Contains("NotFound", StringComparison.Ordinal))
        {
            return null;
        }
    }

    public async Task SinalizarAsync(string id, string sinal, string entrada, CancellationToken cancellationToken)
    {
        var cliente = await _canal.ObterAsync(_opcoes, cancellationToken);
        var handle = cliente.GetWorkflowHandle(id);
        await handle.SignalAsync("receber-sinal", new object[] { sinal + ":" + entrada }, new WorkflowSignalOptions());
    }

    public async Task CancelarAsync(string id, CancellationToken cancellationToken)
    {
        var cliente = await _canal.ObterAsync(_opcoes, cancellationToken);
        var handle = cliente.GetWorkflowHandle(id);
        await handle.CancelAsync();
    }

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        return Verificacao.Executar(
            async token =>
            {
                await _canal.ObterAsync(_opcoes, token);
            },
            cancellationToken);
    }

    public void Dispose()
    {
        _canal.Dispose();
    }

    private sealed class Canal : IDisposable
    {
        private readonly SemaphoreSlim _porta = new(1, 1);
        private TemporalClient? _cliente;

        public async Task<TemporalClient> ObterAsync(OpcoesDeWorkflow opcoes, CancellationToken cancellationToken)
        {
            if (_cliente is not null)
            {
                return _cliente;
            }

            await _porta.WaitAsync(cancellationToken);
            try
            {
                if (_cliente is null)
                {
                    var conexao = new TemporalClientConnectOptions
                    {
                        TargetHost = opcoes.Endpoint,
                        Namespace = opcoes.NamespaceDeTrabalho,
                    };
                    if (opcoes.Tls)
                    {
                        conexao.Tls = new TlsOptions();
                    }

                    _cliente = await TemporalClient.ConnectAsync(conexao);
                }

                return _cliente;
            }
            finally
            {
                _porta.Release();
            }
        }

        public void Dispose()
        {
            _porta.Dispose();
        }
    }

    private static string EstadoDe(string status)
    {
        if (status == "Running")
        {
            return "iniciado";
        }

        if (status == "Completed")
        {
            return "concluido";
        }

        if (status == "Canceled" || status == "Terminated")
        {
            return "cancelado";
        }

        if (status == "Failed" || status == "TimedOut")
        {
            return "falhou";
        }

        return status.ToLowerInvariant();
    }
}
