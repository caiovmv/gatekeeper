namespace gatekeeper.Health;

/// <summary>
/// Um componente em uso na Infrastructure. O health completo chama todas as sondas registradas.
/// </summary>
public interface ISondaDeInfraestrutura
{
    string Nome { get; }

    Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken);
}
