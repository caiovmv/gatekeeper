using gatekeeper.Health;

namespace gatekeeper.Messaging.Adapters;

public static class DestinoDeMensagens
{
    public static OpcoesDeMensagens Montar(string nome, string endpoint, string? token, string? stream, string? subject)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Contains(' '))
        {
            throw new ArgumentException(nome + " exige Endpoint.");
        }

        var url = endpoint.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "nats://" + url;
        }

        var nomeDoStream = string.IsNullOrWhiteSpace(stream) ? StreamPadrao(nome) : stream.Trim();
        var nomeDoSubject = string.IsNullOrWhiteSpace(subject) ? SubjectPadrao(nome) : subject.Trim();
        ExigirToken(nomeDoStream, nome + " tem Stream invalido.");
        ExigirSubject(nomeDoSubject, nome + " tem Subject invalido.");
        var credencial = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        return new OpcoesDeMensagens(nome, url, credencial, nomeDoStream, nomeDoSubject);
    }

    private static string StreamPadrao(string nome)
    {
        if (nome == NomesDeSonda.MensagensExterno)
        {
            return "gatekeeper-externo";
        }

        return "gatekeeper-interno";
    }

    private static string SubjectPadrao(string nome)
    {
        if (nome == NomesDeSonda.MensagensExterno)
        {
            return "gatekeeper.logs";
        }

        return "gatekeeper.audit";
    }

    private static void ExigirToken(string valor, string mensagem)
    {
        foreach (var caractere in valor)
        {
            if (char.IsLetterOrDigit(caractere) || caractere == '-' || caractere == '_')
            {
                continue;
            }

            throw new ArgumentException(mensagem);
        }
    }

    private static void ExigirSubject(string valor, string mensagem)
    {
        if (valor.Contains(' ') || valor.Contains('*') || valor.Contains('>') || valor.Contains('/'))
        {
            throw new ArgumentException(mensagem);
        }
    }
}
