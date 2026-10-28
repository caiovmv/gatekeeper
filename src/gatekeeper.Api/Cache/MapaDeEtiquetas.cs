namespace gatekeeper.Api;

/// <summary>
/// Familia de recurso compartilhada pela API e pela console.
/// A escrita devolve estas etiquetas em X-Cache-Evict. A leitura devolve a familia em X-Cache-Tags.
/// </summary>
public static class MapaDeEtiquetas
{
    public static IReadOnlyList<string> DeLeitura(PathString caminho)
    {
        var familia = Familia(caminho);
        if (familia is null)
        {
            return Array.Empty<string>();
        }

        return new[] { familia };
    }

    public static IReadOnlyList<string> DeEscrita(PathString caminho)
    {
        var familia = Familia(caminho);
        if (familia is null)
        {
            return Array.Empty<string>();
        }

        var etiquetas = new List<string> { familia };
        if (familia == "organizations")
        {
            etiquetas.Add("members");
            etiquetas.Add("machine-keys");
        }
        else if (familia == "ingest")
        {
            etiquetas.Clear();
            etiquetas.Add("audit");
            etiquetas.Add("access-log");
        }

        etiquetas.Add("audit");
        return etiquetas.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string? Familia(PathString caminho)
    {
        var valor = caminho.Value;
        const string prefixo = "/api/";
        if (valor is null || !valor.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resto = valor.Substring(prefixo.Length);
        var barra = resto.IndexOf('/');
        var nome = barra < 0 ? resto : resto.Substring(0, barra);
        if (nome.Length == 0)
        {
            return null;
        }

        var familia = nome.ToLowerInvariant();
        if (familia == "access-logs")
        {
            return "access-log";
        }

        return familia;
    }
}

public static class CabecalhosDeCache
{
    public const string Estado = "X-Cache";

    public const string Etiquetas = "X-Cache-Tags";

    public const string Despejo = "X-Cache-Evict";

    public const string Acerto = "hit";

    public const string Falha = "miss";
}
