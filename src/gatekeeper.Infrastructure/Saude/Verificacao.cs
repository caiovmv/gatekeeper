using gatekeeper.Health;

namespace gatekeeper.Health.Adapters;

public static class Verificacao
{
    public static async Task<EstadoDaSonda> Executar(Func<CancellationToken, Task> acao, CancellationToken cancellationToken)
    {
        try
        {
            await acao(cancellationToken);
            return new EstadoDaSonda(true, "ok");
        }
        catch (Exception excecao)
        {
            return EstadoDaSonda.Falha(excecao);
        }
    }
}
