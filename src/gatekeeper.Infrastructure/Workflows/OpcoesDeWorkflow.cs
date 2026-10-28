namespace gatekeeper.Workflows.Adapters;

public sealed class OpcoesDeWorkflow
{
    public OpcoesDeWorkflow(string endpoint, string namespaceDeTrabalho, string fila, bool tls)
    {
        Endpoint = endpoint;
        NamespaceDeTrabalho = namespaceDeTrabalho;
        Fila = fila;
        Tls = tls;
    }

    public string Endpoint { get; }

    public string NamespaceDeTrabalho { get; }

    public string Fila { get; }

    public bool Tls { get; }
}
