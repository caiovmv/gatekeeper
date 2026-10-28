using gatekeeper.Common;

namespace gatekeeper.Api;

public sealed class ResultadoHttp<T> : IActionResult
{
    private readonly Resultado<T> _resultado;

    public ResultadoHttp(Resultado<T> resultado)
    {
        _resultado = resultado;
    }

    public Task ExecuteResultAsync(ActionContext contexto)
    {
        return RespostaHttp.De(_resultado).ExecuteAsync(contexto.HttpContext);
    }
}
