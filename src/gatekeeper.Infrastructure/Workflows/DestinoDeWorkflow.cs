namespace gatekeeper.Workflows.Adapters;

public static class DestinoDeWorkflow
{
    public static OpcoesDeWorkflow Montar(string endpoint, string? namespaceDeTrabalho, string? fila)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Workflows exige Endpoint.");
        }

        var tls = false;
        var alvo = endpoint.Trim();
        if (alvo.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            tls = true;
            alvo = alvo["https://".Length..];
        }
        else if (alvo.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            alvo = alvo["http://".Length..];
        }

        alvo = alvo.TrimEnd('/');
        if (alvo.Length == 0 || alvo.Contains(' '))
        {
            throw new ArgumentException("Workflows:Endpoint invalido.");
        }

        var espaco = string.IsNullOrWhiteSpace(namespaceDeTrabalho) ? "default" : namespaceDeTrabalho.Trim();
        var nomeDaFila = string.IsNullOrWhiteSpace(fila) ? "gatekeeper" : fila.Trim();
        Exigir(espaco, "Workflows:Namespace invalido.");
        Exigir(nomeDaFila, "Workflows:Queue invalida.");
        return new OpcoesDeWorkflow(alvo, espaco, nomeDaFila, tls);
    }

    private static void Exigir(string valor, string mensagem)
    {
        if (valor.Contains(' ') || valor.Contains('/') || valor.Contains('?'))
        {
            throw new ArgumentException(mensagem);
        }
    }
}
