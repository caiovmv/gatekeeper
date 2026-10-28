namespace gatekeeper.Common;

/// <summary>
/// Identidade de um agregado. A regra mora na classe que herda isto, nao no repositorio.
/// </summary>
public abstract class Entidade
{
    protected Entidade(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
}
