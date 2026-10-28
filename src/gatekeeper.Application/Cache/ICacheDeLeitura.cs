namespace gatekeeper.Caching;

/// <summary>
/// Leitura de JSON com prazo e despejo por etiqueta. A entrada morre aos cinco minutos
/// mesmo quando o evento de escrita nao chega.
/// </summary>
public interface ICacheDeLeitura
{
    Task<LeituraGuardada?> ObterAsync(string chave, CancellationToken cancellationToken);

    Task GravarAsync(string chave, LeituraGuardada leitura, IReadOnlyCollection<string> etiquetas, CancellationToken cancellationToken);

    Task RemoverPorEtiquetasAsync(IReadOnlyCollection<string> etiquetas, CancellationToken cancellationToken);
}

public sealed record LeituraGuardada(int Status, string Tipo, string Corpo, string Etiquetas);

public static class PoliticaDeCache
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(5);

    public const int TamanhoMaximo = 256 * 1024;
}
