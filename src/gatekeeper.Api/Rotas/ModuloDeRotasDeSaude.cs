using gatekeeper.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace gatekeeper.Api;

public sealed class ModuloDeRotasDeSaude : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/health", ConsultarProcessoAsync)
            .WithName("Saude")
            .WithTags("Saude")
            .Produces<RelatorioDeSaude>(StatusCodes.Status200OK)
            .Produces<RelatorioDeSaude>(StatusCodes.Status503ServiceUnavailable);

        rotas.MapGet("/health/ready", ConsultarProntaAsync)
            .WithName("SaudePronta")
            .WithTags("Saude")
            .Produces<RelatorioDeSaude>(StatusCodes.Status200OK)
            .Produces<RelatorioDeSaude>(StatusCodes.Status503ServiceUnavailable);

        rotas.MapGet("/health/complete", ConsultarCompletaAsync)
            .WithName("SaudeCompleta")
            .WithTags("Saude")
            .Produces<RelatorioDeSaude>(StatusCodes.Status200OK)
            .Produces<RelatorioDeSaude>(StatusCodes.Status503ServiceUnavailable);
    }

    private static Task<IResult> ConsultarProcessoAsync(HealthCheckService saude, CancellationToken cancellationToken)
    {
        return ConsultarAsync(saude, "live", cancellationToken);
    }

    private static Task<IResult> ConsultarProntaAsync(HealthCheckService saude, CancellationToken cancellationToken)
    {
        return ConsultarAsync(saude, "ready", cancellationToken);
    }

    private static Task<IResult> ConsultarCompletaAsync(HealthCheckService saude, CancellationToken cancellationToken)
    {
        return ConsultarAsync(saude, "complete", cancellationToken);
    }

    private static async Task<IResult> ConsultarAsync(HealthCheckService saude, string etiqueta, CancellationToken cancellationToken)
    {
        var relatorio = await saude.CheckHealthAsync(
            registro => registro.Tags.Contains(etiqueta),
            cancellationToken);
        var corpo = EscritorDeSaude.Montar(relatorio);
        if (relatorio.Status == HealthStatus.Unhealthy)
        {
            return Results.Json(corpo, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(corpo);
    }
}
