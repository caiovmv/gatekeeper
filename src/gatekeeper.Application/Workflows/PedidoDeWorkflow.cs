namespace gatekeeper.Workflows;

public sealed class WorkflowRequest
{
    public WorkflowRequest(string nome, string? id, string entrada)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O workflow precisa de um nome.", nameof(nome));
        }

        Nome = nome.Trim();
        Id = id ?? string.Empty;
        Entrada = entrada ?? string.Empty;
    }

    public string Nome { get; }

    public string Id { get; }

    public string Entrada { get; }
}
