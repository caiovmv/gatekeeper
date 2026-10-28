using System.Diagnostics;
using gatekeeper.Identity;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace gatekeeper.Observability.Adapters;

public sealed class ProcessadorDeRedacao : BaseProcessor<Activity>
{
    public override void OnEnd(Activity atividade)
    {
        Aplicar(atividade);
    }

    public static void Aplicar(Activity atividade)
    {
        var tags = new List<KeyValuePair<string, object?>>();
        foreach (var tag in atividade.TagObjects)
        {
            tags.Add(tag);
        }

        foreach (var tag in tags)
        {
            if (EhConsulta(tag.Key) || EhCabecalhoSecreto(tag.Key))
            {
                atividade.SetTag(tag.Key, null);
                continue;
            }

            if (EhUrl(tag.Key) && tag.Value is string url)
            {
                atividade.SetTag(tag.Key, SemConsulta(url));
                continue;
            }

            if (tag.Value is string texto)
            {
                var redigido = RedacaoDeSegredo.Aplicar(texto);
                if (!string.Equals(redigido, texto, StringComparison.Ordinal))
                {
                    atividade.SetTag(tag.Key, redigido);
                }
            }
        }
    }

    private static bool EhConsulta(string chave)
    {
        return chave.Equals("url.query", StringComparison.OrdinalIgnoreCase)
            || chave.Equals("http.query", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EhUrl(string chave)
    {
        return chave.Equals("url.full", StringComparison.OrdinalIgnoreCase)
            || chave.Equals("http.url", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EhCabecalhoSecreto(string chave)
    {
        if (chave.Contains("authorization", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (chave.Contains("api-key", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return chave.Contains("x-api-key", StringComparison.OrdinalIgnoreCase);
    }

    private static string SemConsulta(string url)
    {
        var interrogacao = url.IndexOf('?', StringComparison.Ordinal);
        if (interrogacao >= 0)
        {
            url = url.Substring(0, interrogacao);
        }

        return RedacaoDeSegredo.Aplicar(url);
    }
}

public sealed class ProcessadorDeLog : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord registro)
    {
        if (!string.IsNullOrEmpty(registro.FormattedMessage))
        {
            registro.FormattedMessage = RedacaoDeSegredo.Aplicar(registro.FormattedMessage);
        }

        if (registro.Body is string corpo)
        {
            registro.Body = RedacaoDeSegredo.Aplicar(corpo);
        }
    }
}
