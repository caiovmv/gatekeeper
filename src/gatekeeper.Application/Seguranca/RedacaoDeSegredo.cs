using System.Text.RegularExpressions;

namespace gatekeeper.Identity;

public static class RedacaoDeSegredo
{
    private static readonly Regex Atribuicao = new(
        @"(?i)(password|pwd|api-key|token|ficha)\s*[:=]\s*\S+",
        RegexOptions.Compiled);

    private static readonly Regex Bearer = new(
        @"Bearer\s+\S+",
        RegexOptions.Compiled);

    public static string Aplicar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var documento = MascaraDeDocumento.Aplicar(texto);
        var semAtribuicao = Atribuicao.Replace(documento, "$1=***");
        return Bearer.Replace(semAtribuicao, "Bearer ***");
    }
}
