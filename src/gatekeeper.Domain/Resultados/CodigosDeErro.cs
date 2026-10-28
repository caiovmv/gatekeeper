namespace gatekeeper.Common;

/// <summary>
/// Codigos estaveis. A borda HTTP traduz o codigo em status. O dominio nao conhece HTTP.
/// </summary>
public static class CodigosDeErro
{
    public const string NaoEncontrado = "nao_encontrado";

    public const string Validacao = "validacao";

    public const string Conflito = "conflito";

    public const string Inesperado = "inesperado";

    public const string NaoAutorizado = "nao_autorizado";
}
