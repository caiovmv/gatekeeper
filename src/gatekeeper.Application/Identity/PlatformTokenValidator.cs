using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gatekeeper.Common;

namespace gatekeeper.Identity;

public sealed class PlatformTokenValidator : IPlatformTokenValidator
{
    private readonly PlatformTokenOptions _options;
    private readonly ISigningKeySource _keys;

    public PlatformTokenValidator(PlatformTokenOptions options, ISigningKeySource keys)
    {
        _options = options;
        _keys = keys;
    }

    public async Task<Resultado<PlatformPrincipal>> ValidateAsync(string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_options.Issuer))
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected"));
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected"));
        }

        var headerText = Base64Url.DecodeText(parts[0]);
        var payloadText = Base64Url.DecodeText(parts[1]);
        var signature = Base64Url.Decode(parts[2]);
        if (headerText is null || payloadText is null || signature is null)
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected"));
        }

        if (!ReadHeader(headerText, out var keyId))
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected"));
        }

        var key = await _keys.FindAsync(keyId, cancellationToken);
        if (key is null)
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected"));
        }

        var input = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        if (!key.VerifyData(input, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected"));
        }

        if (!ReadPayload(payloadText, now, out var subject, out var organization, out var roles))
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected"));
        }

        return Resultado<PlatformPrincipal>.Ok(new PlatformPrincipal(subject, organization, _options.Issuer, roles));
    }

    private static bool ReadHeader(string header, out string keyId)
    {
        keyId = string.Empty;
        try
        {
            using var json = JsonDocument.Parse(header);
            if (!json.RootElement.TryGetProperty("alg", out var algorithm) || !string.Equals(algorithm.GetString(), "RS256", StringComparison.Ordinal))
            {
                return false;
            }

            if (json.RootElement.TryGetProperty("kid", out var id) && id.ValueKind == JsonValueKind.String)
            {
                keyId = id.GetString() ?? string.Empty;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool ReadPayload(string payload, DateTimeOffset now, out string subject, out string organization, out IReadOnlyList<string> roles)
    {
        subject = string.Empty;
        organization = string.Empty;
        roles = Array.Empty<string>();
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (!root.TryGetProperty("iss", out var issuer) || !string.Equals(issuer.GetString(), _options.Issuer, StringComparison.Ordinal))
            {
                return false;
            }

            if (!AudienceMatches(root))
            {
                return false;
            }

            if (!root.TryGetProperty("exp", out var expiry) || !expiry.TryGetInt64(out var exp) || now.ToUnixTimeSeconds() >= exp)
            {
                return false;
            }

            if (!root.TryGetProperty("sub", out var sub) || sub.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            subject = sub.GetString() ?? string.Empty;
            if (subject.Length == 0)
            {
                return false;
            }

            organization = ReadOrganization(root);
            roles = ReadRoles(root);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool AudienceMatches(JsonElement root)
    {
        if (_options.Audience.Length == 0)
        {
            return true;
        }

        if (!root.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        if (audience.ValueKind == JsonValueKind.String)
        {
            return _options.Audiences.Contains(audience.GetString() ?? string.Empty, StringComparer.Ordinal);
        }

        if (audience.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in audience.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && _options.Audiences.Contains(item.GetString() ?? string.Empty, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadOrganization(JsonElement root)
    {
        if (root.TryGetProperty("urn:zitadel:iam:org:id", out var zitadel) && zitadel.ValueKind == JsonValueKind.String)
        {
            return zitadel.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("org", out var org) && org.ValueKind == JsonValueKind.String)
        {
            return org.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ReadRoles(JsonElement root)
    {
        if (root.TryGetProperty("urn:zitadel:iam:org:project:roles", out var zitadel) && zitadel.ValueKind == JsonValueKind.Object)
        {
            var names = new List<string>();
            foreach (var role in zitadel.EnumerateObject())
            {
                names.Add(role.Name);
            }

            return names;
        }

        if (!root.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var role in roles.EnumerateArray())
        {
            if (role.ValueKind == JsonValueKind.String)
            {
                var name = role.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(name);
                }
            }
        }

        return list;
    }
}

public sealed class InactivePlatformTokenValidator : IPlatformTokenValidator
{
    public Task<Resultado<PlatformPrincipal>> ValidateAsync(string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("platform token rejected")));
    }
}

public sealed class StaticSigningKeySource : ISigningKeySource
{
    private readonly RSA _key;

    public StaticSigningKeySource(string modulus, string exponent)
    {
        var n = Base64Url.Decode(modulus);
        var e = Base64Url.Decode(exponent);
        if (n is null || e is null)
        {
            throw new InvalidOperationException("Identity public key is incomplete.");
        }

        _key = RSA.Create();
        _key.ImportParameters(new RSAParameters
        {
            Modulus = n,
            Exponent = e,
        });
    }

    public Task<RSA?> FindAsync(string? keyId, CancellationToken cancellationToken)
    {
        return Task.FromResult<RSA?>(_key);
    }
}

public sealed class EmptySigningKeySource : ISigningKeySource
{
    public Task<RSA?> FindAsync(string? keyId, CancellationToken cancellationToken)
    {
        return Task.FromResult<RSA?>(null);
    }
}
