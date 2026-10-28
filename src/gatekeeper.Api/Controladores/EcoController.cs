namespace gatekeeper.Api;

/// <summary>Controller concreto exigido pelo teste de arquitetura. Nao expoe produto.</summary>
[ApiController]
[Route("api/esqueleto/eco")]
public sealed class EcoController : ControladorDeApi
{
    [HttpGet]
    public IActionResult Obter()
    {
        return Ok(new { status = "ok" });
    }
}
