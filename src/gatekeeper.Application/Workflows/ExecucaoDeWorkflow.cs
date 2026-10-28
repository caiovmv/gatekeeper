namespace gatekeeper.Workflows;

public sealed record WorkflowRun(string Id, string Nome, string Estado, string? Resultado = null, string? Sinal = null);
