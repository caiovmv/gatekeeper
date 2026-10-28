using System.Collections.Concurrent;
using gatekeeper.Health;
using gatekeeper.Workflows;

namespace gatekeeper.Workflows.Adapters;

/// <summary>
/// Executa a atividade no processo e devolve concluido. O Temporal devolve iniciado ate o worker terminar.
/// </summary>
public sealed class InMemoryWorkflowEngine : IWorkflowEngine, ISondaDeInfraestrutura
{
    private readonly ConcurrentDictionary<string, WorkflowRun> _execucoes = new();
    private readonly AtividadeDeEco _atividade = new();

    public string Nome => NomesDeSonda.Workflows;

    public Task<WorkflowRun> IniciarAsync(WorkflowRequest pedido, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = string.IsNullOrWhiteSpace(pedido.Id) ? Guid.NewGuid().ToString("N") : pedido.Id.Trim();
        var execucao = new WorkflowRun(id, pedido.Nome, "concluido", _atividade.Responder(pedido.Entrada));
        if (!_execucoes.TryAdd(id, execucao))
        {
            throw new InvalidOperationException("Ja existe uma execucao com este id.");
        }

        return Task.FromResult(execucao);
    }

    public Task<WorkflowRun?> ConsultarAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_execucoes.TryGetValue(id, out var execucao))
        {
            return Task.FromResult<WorkflowRun?>(execucao);
        }

        return Task.FromResult<WorkflowRun?>(null);
    }

    public Task SinalizarAsync(string id, string sinal, string entrada, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_execucoes.TryGetValue(id, out var execucao))
        {
            throw new InvalidOperationException("Execucao '" + id + "' nao existe.");
        }

        _execucoes[id] = execucao with { Sinal = sinal + ":" + entrada };
        return Task.CompletedTask;
    }

    public Task CancelarAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_execucoes.TryGetValue(id, out var execucao))
        {
            throw new InvalidOperationException("Execucao '" + id + "' nao existe.");
        }

        if (execucao.Estado == "concluido" || execucao.Estado == "cancelado")
        {
            throw new InvalidOperationException("Execucao '" + id + "' ja encerrou.");
        }

        _execucoes[id] = execucao with { Estado = "cancelado" };
        return Task.CompletedTask;
    }

    public WorkflowRun? Obter(string id)
    {
        return ConsultarAsync(id, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EstadoDaSonda(true, "memoria"));
    }
}
