using gatekeeper.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace gatekeeper.Messaging.Adapters;

public sealed class DespachanteDeCaixa : BackgroundService
{
    private readonly IOutbox _caixa;
    private readonly ILogger<DespachanteDeCaixa> _logger;

    public DespachanteDeCaixa(IOutbox caixa, ILogger<DespachanteDeCaixa> logger)
    {
        _caixa = caixa;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await relogio.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _caixa.DespacharAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception excecao)
            {
                _logger.LogError(excecao, "A caixa de saida nao publicou neste ciclo.");
            }
        }
    }
}
