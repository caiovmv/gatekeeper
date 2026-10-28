using System.Security.Cryptography;
using System.Text;

namespace gatekeeper.Identity;

public readonly record struct ParametroDeWebhook(string Nome, string Valor);

public static class AssinaturaTwilio
{
    public static bool Confere(string token, string url, IReadOnlyList<ParametroDeWebhook> parametros, string assinatura)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(assinatura))
        {
            return false;
        }

        var esperada = Calcular(token, url, parametros);
        var a = Encoding.UTF8.GetBytes(esperada);
        var b = Encoding.UTF8.GetBytes(assinatura.Trim());
        if (a.Length != b.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string Calcular(string token, string url, IReadOnlyList<ParametroDeWebhook> parametros)
    {
        var ordenados = new List<ParametroDeWebhook>(parametros);
        ordenados.Sort(static (esquerda, direita) => string.CompareOrdinal(esquerda.Nome, direita.Nome));
        var material = new StringBuilder(url);
        foreach (var parametro in ordenados)
        {
            material.Append(parametro.Nome);
            material.Append(parametro.Valor);
        }

        var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, Encoding.UTF8.GetBytes(token));
        hmac.AppendData(Encoding.UTF8.GetBytes(material.ToString()));
        return Convert.ToBase64String(hmac.GetHashAndReset());
    }
}
