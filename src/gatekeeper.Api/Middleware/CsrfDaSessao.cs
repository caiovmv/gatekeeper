using System.Security.Cryptography;

namespace gatekeeper.Api;

public sealed class CsrfDaSessao
{
    private readonly RequestDelegate _proximo;

    public CsrfDaSessao(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        var metodo = contexto.Request.Method;
        var mutacao = HttpMethods.IsPost(metodo) || HttpMethods.IsPut(metodo) || HttpMethods.IsDelete(metodo) || HttpMethods.IsPatch(metodo);
        var bearer = contexto.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var sessao = contexto.Request.Cookies.ContainsKey("gatekeeper_session");
        if (mutacao && sessao && !bearer)
        {
            var cookie = contexto.Request.Cookies["gatekeeper_csrf"] ?? string.Empty;
            var header = contexto.Request.Headers["X-CSRF"].ToString();
            var left = System.Text.Encoding.UTF8.GetBytes(cookie);
            var right = System.Text.Encoding.UTF8.GetBytes(header);
            if (cookie.Length == 0 || left.Length != right.Length || !CryptographicOperations.FixedTimeEquals(left, right))
            {
                contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
                await contexto.Response.WriteAsJsonAsync(new { status = 403, detail = "csrf" });
                return;
            }
        }

        await _proximo(contexto);
    }
}

public static class CsrfDaSessaoExtensions
{
    public static WebApplication UseCsrfDaSessao(this WebApplication app)
    {
        app.UseMiddleware<CsrfDaSessao>();
        return app;
    }
}
