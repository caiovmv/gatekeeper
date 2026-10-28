using Temporalio.Activities;

namespace gatekeeper.Workflows.Adapters;

public sealed class AtividadeDeEco
{
    [Activity]
    public string Responder(string entrada)
    {
        return "eco:" + entrada;
    }
}
