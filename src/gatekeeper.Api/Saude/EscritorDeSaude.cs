using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace gatekeeper.Health;

public static class EscritorDeSaude
{
    public static RelatorioDeSaude Montar(HealthReport relatorio)
    {
        var componentes = new Dictionary<string, ComponenteDeSaude>();
        foreach (var entrada in relatorio.Entries)
        {
            componentes[entrada.Key] = new ComponenteDeSaude(
                entrada.Value.Status.ToString(),
                entrada.Value.Description ?? string.Empty);
        }

        return new RelatorioDeSaude(relatorio.Status.ToString(), componentes);
    }
}
