namespace gatekeeper.Common;

public sealed class Pagina<T>
{
    public Pagina(IReadOnlyList<T> itens, Recorte recorte)
    {
        Itens = itens;
        Recorte = recorte;
    }

    public IReadOnlyList<T> Itens { get; }

    public Recorte Recorte { get; }
}
