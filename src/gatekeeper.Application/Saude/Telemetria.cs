using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace gatekeeper.Observability;

public interface ITelemetry
{
    void Rejeicao(string codigo);

    void SondaFalha(string componente);
}

public static class TelemetryCatalog
{
    public const string Medidor = "gatekeeper";

    public const string Fonte = "gatekeeper";
}

public sealed class TelemetriaDeServico : ITelemetry, IDisposable
{
    private readonly Meter _medidor;
    private readonly Counter<long> _rejeicoes;
    private readonly Counter<long> _sondas;

    public TelemetriaDeServico()
    {
        _medidor = new Meter(TelemetryCatalog.Medidor);
        _rejeicoes = _medidor.CreateCounter<long>("rejeicoes");
        _sondas = _medidor.CreateCounter<long>("sonda_falha");
    }

    public void Rejeicao(string codigo)
    {
        _rejeicoes.Add(1, new KeyValuePair<string, object?>("codigo", codigo));
    }

    public void SondaFalha(string componente)
    {
        _sondas.Add(1, new KeyValuePair<string, object?>("componente", componente));
    }

    public void Dispose()
    {
        _medidor.Dispose();
    }
}
