using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace gatekeeper.Identity;

public sealed class SigningKey
{
    public SigningKey(string keyId, RSA key)
    {
        KeyId = keyId;
        Key = key;
    }

    public string KeyId { get; }

    public RSA Key { get; }
}

public static class JwksDocument
{
    public static IReadOnlyList<SigningKey> Read(string json)
    {
        var keys = new List<SigningKey>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("keys", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return keys;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (!item.TryGetProperty("kty", out var kind) || !string.Equals(kind.GetString(), "RSA", StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetProperty("n", out var modulus) || !item.TryGetProperty("e", out var exponent))
            {
                continue;
            }

            var n = Base64Url.Decode(modulus.GetString() ?? string.Empty);
            var e = Base64Url.Decode(exponent.GetString() ?? string.Empty);
            if (n is null || e is null)
            {
                continue;
            }

            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = n,
                Exponent = e,
            });
            var kid = item.TryGetProperty("kid", out var id) ? id.GetString() ?? string.Empty : string.Empty;
            keys.Add(new SigningKey(kid, rsa));
        }

        return keys;
    }
}

public static class Base64Url
{
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[]? Decode(string text)
    {
        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            var remainder = padded.Length % 4;
            if (remainder == 1)
            {
                return null;
            }

            if (remainder > 0)
            {
                padded = padded.PadRight(padded.Length + (4 - remainder), '=');
            }

            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string? DecodeText(string text)
    {
        var bytes = Decode(text);
        if (bytes is null)
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
