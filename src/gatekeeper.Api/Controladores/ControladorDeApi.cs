using gatekeeper.Api;
using gatekeeper.Common;

namespace gatekeeper.Api;

/// <summary>
/// Traduz o resultado do servico. Nao abre banco, nao decide regra.
/// </summary>
public abstract class ControladorDeApi : ControllerBase
{
    protected IActionResult Responder<T>(Resultado<T> resultado)
    {
        return new ResultadoHttp<T>(resultado);
    }
}
