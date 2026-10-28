using System.Security.Cryptography;
using System.Text;
using gatekeeper.Common;

namespace gatekeeper.Identity;

public static class FichaDeBrowser
{
    public static Resultado<string> Emitir(string chave, Guid chamada, DateTimeOffset expira, string origem)
    {
        if (string.IsNullOrWhiteSpace(chave) || string.IsNullOrWhiteSpace(origem))
        {
            return Resultado<string>.Falha(Erro.Validacao("ficha incompleta"));
        }

        var carga = chamada.ToString("N") + "|" + expira.ToUnixTimeSeconds().ToString() + "|" + origem.Trim();
        var mac = Assinar(chave, carga);
        return Resultado<string>.Ok(Codificar(carga) + "." + Codificar(mac));
    }

    public static Resultado<Guid> Validar(string chave, string ficha, DateTimeOffset agora, string origem)
    {
        if (string.IsNullOrWhiteSpace(chave) || string.IsNullOrWhiteSpace(ficha) || string.IsNullOrWhiteSpace(origem))
        {
            return Resultado<Guid>.Falha(Erro.NaoAutorizado("ficha recusada"));
        }

        var partes = ficha.Split('.');
        if (partes.Length != 2)
        {
            return Resultado<Guid>.Falha(Erro.NaoAutorizado("ficha recusada"));
        }

        var cargaBytes = Decodificar(partes[0]);
        var mac = Decodificar(partes[1]);
        if (cargaBytes is null || mac is null)
        {
            return Resultado<Guid>.Falha(Erro.NaoAutorizado("ficha recusada"));
        }

        var carga = Encoding.UTF8.GetString(cargaBytes);
        if (!Confere(Assinar(chave, carga), mac))
        {
            return Resultado<Guid>.Falha(Erro.NaoAutorizado("ficha recusada"));
        }

        var campos = carga.Split('|');
        if (campos.Length != 3
            || !Guid.TryParseExact(campos[0], "N", out var chamada)
            || !long.TryParse(campos[1], out var unix)
            || !string.Equals(campos[2], origem.Trim(), StringComparison.Ordinal))
        {
            return Resultado<Guid>.Falha(Erro.NaoAutorizado("ficha recusada"));
        }

        if (agora.ToUnixTimeSeconds() >= unix)
        {
            return Resultado<Guid>.Falha(Erro.NaoAutorizado("ficha expirada"));
        }

        return Resultado<Guid>.Ok(chamada);
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
