namespace gatekeeper.Workflows;

/// <summary>
/// Start, query, signal and cancel. The worker runs the activity on the same queue.
/// </summary>
public interface IWorkflowEngine
{
    Task<WorkflowRun> IniciarAsync(WorkflowRequest pedido, CancellationToken cancellationToken);

    Task<WorkflowRun?> ConsultarAsync(string id, CancellationToken cancellationToken);

    Task SinalizarAsync(string id, string sinal, string entrada, CancellationToken cancellationToken);

    Task CancelarAsync(string id, CancellationToken cancellationToken);
}
