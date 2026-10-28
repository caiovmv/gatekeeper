namespace gatekeeper.Caching.Adapters;

public static class ProvedoresDeCache
{
    public const string Redis = "Redis";

    public const string Valkey = "Valkey";

    public static string Canonico(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
        {
            throw new ArgumentException("Caching:Provider e obrigatorio.", nameof(provedor));
        }

        if (Eh(provedor, "Redis"))
        {
            return Redis;
        }

        if (Eh(provedor, "Valkey"))
        {
            return Valkey;
        }

        throw new ArgumentException("Caching:Provider '" + provedor + "' nao e Redis nem Valkey.");
    }

    private static bool Eh(string valor, string esperado)
    {
        return string.Equals(valor.Trim(), esperado, StringComparison.OrdinalIgnoreCase);
    }
}
