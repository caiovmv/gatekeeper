using Temporalio.Workflows;

namespace gatekeeper.Workflows.Adapters;

[Workflow]
public class FluxoDeAtividade
{
    private string _sinal = string.Empty;

    [WorkflowRun]
    public async Task<string> ExecutarAsync(string entrada)
    {
        return await Workflow.ExecuteActivityAsync(
            (AtividadeDeEco atividade) => atividade.Responder(entrada),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(1) });
    }

    [WorkflowSignal("receber-sinal")]
    public Task ReceberSinalAsync(string texto)
    {
        _sinal = texto ?? string.Empty;
        return Task.CompletedTask;
    }

    [WorkflowQuery("sinal")]
    public string LerSinal()
    {
        return _sinal;
    }
}
