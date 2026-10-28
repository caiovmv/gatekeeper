using gatekeeper.Health;

namespace gatekeeper.Health.Adapters;

public sealed class SondaEmMemoria : ISondaDeInfraestrutura
{
    private readonly string _nome;

    public SondaEmMemoria(string nome)
    {
        _nome = nome;
    }

    public string Nome => _nome;

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EstadoDaSonda(true, "memoria"));
    }
}
