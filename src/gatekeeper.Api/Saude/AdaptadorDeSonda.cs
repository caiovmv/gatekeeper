using gatekeeper.Health;
using gatekeeper.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace gatekeeper.Health;

public sealed class AdaptadorDeSonda : IHealthCheck
{
    private readonly ISondaDeInfraestrutura _sonda;
    private readonly ITelemetry _telemetria;

    public AdaptadorDeSonda(ISondaDeInfraestrutura sonda, ITelemetry telemetria)
    {
        _sonda = sonda;
        _telemetria = telemetria;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var estado = await _sonda.VerificarAsync(cancellationToken);
        if (estado.Saudavel)
        {
            return HealthCheckResult.Healthy(estado.Detalhe);
        }

        _telemetria.SondaFalha(_sonda.Nome);
        return HealthCheckResult.Unhealthy(estado.Detalhe);
    }
}
