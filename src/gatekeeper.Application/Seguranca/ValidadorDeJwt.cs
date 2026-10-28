using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gatekeeper.Common;

namespace gatekeeper.Identity;

public static class ValidadorDeJwt
{
    public static Resultado<string> Emitir(string emissor, string audiencia, string chave, DateTimeOffset expira)
    {
        if (string.IsNullOrWhiteSpace(emissor) || string.IsNullOrWhiteSpace(audiencia) || string.IsNullOrWhiteSpace(chave))
        {
            return Resultado<string>.Falha(Erro.Validacao("jwt incompleto"));
        }

        var cabecalho = Codificar("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var carga = Codificar(
            "{\"iss\":"
            + JsonSerializer.Serialize(emissor)
            + ",\"aud\":"
            + JsonSerializer.Serialize(audiencia)
            + ",\"exp\":"
            + expira.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            + "}");
        var assinatura = Codificar(Assinar(chave, cabecalho + "." + carga));
        return Resultado<string>.Ok(cabecalho + "." + carga + "." + assinatura);
    }

    public static Resultado<string> EmitirSessao(string emissor, string audiencia, string chave, DateTimeOffset expira, string subject, string organization, IReadOnlyList<string> roles, string email = "")
    {
        if (string.IsNullOrWhiteSpace(emissor) || string.IsNullOrWhiteSpace(audiencia) || string.IsNullOrWhiteSpace(chave) || string.IsNullOrWhiteSpace(subject))
        {
            return Resultado<string>.Falha(Erro.Validacao("jwt incompleto"));
        }

        var cabecalho = Codificar("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var papeis = JsonSerializer.Serialize(roles);
        var correio = email.Trim().Length == 0 ? string.Empty : ",\"email\":" + JsonSerializer.Serialize(email.Trim());
        var carga = Codificar(
            "{\"iss\":"
            + JsonSerializer.Serialize(emissor)
            + ",\"aud\":"
            + JsonSerializer.Serialize(audiencia)
            + ",\"sub\":"
            + JsonSerializer.Serialize(subject)
            + ",\"org\":"
            + JsonSerializer.Serialize(organization)
            + ",\"roles\":"
            + papeis
            + correio
            + ",\"jti\":"
            + JsonSerializer.Serialize(Guid.NewGuid().ToString("N"))
            + ",\"exp\":"
            + expira.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            + "}");
        var assinatura = Codificar(Assinar(chave, cabecalho + "." + carga));
        return Resultado<string>.Ok(cabecalho + "." + carga + "." + assinatura);
    }

    public static Resultado<PlatformPrincipal> LerSessao(string token, string emissor, string audiencia, string chave, DateTimeOffset agora)
    {
        var valido = Validar(token, emissor, audiencia, chave, agora);
        if (!valido.Sucesso)
        {
            return Resultado<PlatformPrincipal>.Falha(valido.Erro);
        }

        var carga = DecodificarTexto(token.Split('.')[1]);
        if (carga is null || !LerSessao(carga, out var subject, out var organization, out var roles, out var email))
        {
            return Resultado<PlatformPrincipal>.Falha(Erro.NaoAutorizado("jwt recusado"));
        }

        return Resultado<PlatformPrincipal>.Ok(new PlatformPrincipal(subject, organization, emissor, roles, email));
    }

    public static Resultado<string> Validar(string token, string emissor, string audiencia, string chave, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(emissor) || string.IsNullOrWhiteSpace(audiencia) || string.IsNullOrWhiteSpace(chave))
        {
            return Resultado<string>.Falha(Erro.NaoAutorizado("jwt recusado"));
        }

        var partes = token.Split('.');
        if (partes.Length != 3)
        {
            return Resultado<string>.Falha(Erro.NaoAutorizado("jwt recusado"));
        }

        var cabecalho = DecodificarTexto(partes[0]);
        var carga = DecodificarTexto(partes[1]);
        var assinatura = Decodificar(partes[2]);
        if (cabecalho is null || carga is null || assinatura is null || !AlgoritmoHs256(cabecalho))
        {
            return Resultado<string>.Falha(Erro.NaoAutorizado("jwt recusado"));
        }

        var esperado = Assinar(chave, partes[0] + "." + partes[1]);
        if (!Confere(esperado, assinatura))
        {
            return Resultado<string>.Falha(Erro.NaoAutorizado("jwt recusado"));
        }

        if (!LerReivindicacoes(carga, out var iss, out var aud, out var exp))
        {
            return Resultado<string>.Falha(Erro.NaoAutorizado("jwt recusado"));
        }

        if (!string.Equals(iss, emissor, StringComparison.Ordinal) || !string.Equals(aud, audiencia, StringComparison.Ordinal))
        {
            return Resultado<string>.Falha(Erro.NaoAutorizado("jwt recusado"));
        }

        if (agora.ToUnixTimeSeconds() >= exp)
        {
            return Resultado<string>.Falha(Erro.NaoAutorizado("jwt expirado"));
        }

        return Resultado<string>.Ok(iss);
    }

    private static bool AlgoritmoHs256(string cabecalho)
    {
        try
        {
            using var json = JsonDocument.Parse(cabecalho);
            if (!json.RootElement.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return string.Equals(alg.GetString(), "HS256", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LerReivindicacoes(string carga, out string iss, out string aud, out long exp)
    {
        iss = string.Empty;
        aud = string.Empty;
        exp = 0;
        try
        {
            using var json = JsonDocument.Parse(carga);
            var raiz = json.RootElement;
            if (!raiz.TryGetProperty("iss", out var emissor) || emissor.ValueKind != JsonValueKind.String
                || !raiz.TryGetProperty("aud", out var audiencia) || audiencia.ValueKind != JsonValueKind.String
                || !raiz.TryGetProperty("exp", out var prazo) || !prazo.TryGetInt64(out exp))
            {
                return false;
            }

            iss = emissor.GetString() ?? string.Empty;
            aud = audiencia.GetString() ?? string.Empty;
            return iss.Length > 0 && aud.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LerSessao(string carga, out string subject, out string organization, out string[] roles, out string email)
    {
        subject = string.Empty;
        organization = string.Empty;
        roles = Array.Empty<string>();
        email = string.Empty;
        try
        {
            using var json = JsonDocument.Parse(carga);
            var raiz = json.RootElement;
            if (!raiz.TryGetProperty("sub", out var sujeito) || sujeito.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            subject = sujeito.GetString() ?? string.Empty;
            if (raiz.TryGetProperty("org", out var org) && org.ValueKind == JsonValueKind.String)
            {
                organization = org.GetString() ?? string.Empty;
            }

            if (raiz.TryGetProperty("roles", out var lista) && lista.ValueKind == JsonValueKind.Array)
            {
                var encontrados = new List<string>();
                foreach (var item in lista.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is string papel && papel.Length > 0)
                    {
                        encontrados.Add(papel);
                    }
                }

                roles = encontrados.ToArray();
            }

            if (raiz.TryGetProperty("email", out var correio) && correio.ValueKind == JsonValueKind.String)
            {
                email = correio.GetString() ?? string.Empty;
            }

            return subject.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static byte[] Assinar(string chave, string carga)
    {
        var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(chave));
        hmac.AppendData(Encoding.UTF8.GetBytes(carga));
        return hmac.GetHashAndReset();
    }

    private static bool Confere(byte[] esperado, byte[] recebido)
    {
        if (esperado.Length != recebido.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }

    private static string Codificar(string texto)
    {
        return Codificar(Encoding.UTF8.GetBytes(texto));
    }

    private static string Codificar(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string? DecodificarTexto(string texto)
    {
        var bytes = Decodificar(texto);
        if (bytes is null)
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static byte[]? Decodificar(string texto)
    {
        try
        {
            var padded = texto.Replace('-', '+').Replace('_', '/');
            var resto = padded.Length % 4;
            if (resto == 1)
            {
                return null;
            }

            if (resto > 0)
            {
                padded = padded.PadRight(padded.Length + (4 - resto), '=');
            }

            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
