namespace gatekeeper.Common;

/// <summary>
/// Numero e tamanho de uma listagem. Andam juntos para a pagina nao carregar campos soltos.
/// </summary>
public readonly record struct Recorte(int Pagina, int Tamanho);
