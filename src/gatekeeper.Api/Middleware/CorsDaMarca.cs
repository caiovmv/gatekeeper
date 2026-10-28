namespace gatekeeper.Api;

/// <summary>
/// A origem so entra quando for um dominio cadastrado. Sem cadastro, a resposta nao devolve CORS.
/// </summary>
public sealed class CorsDaMarca
{
    private readonly RequestDelegate _proximo;

    public CorsDaMarca(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers.Remove("Access-Control-Allow-Origin");
            return Task.CompletedTask;
        });
        await _proximo(contexto);
    }
}

public static class CorsDaMarcaExtensions
{
    public static WebApplication UseCorsDaMarca(this WebApplication app)
    {
        app.UseMiddleware<CorsDaMarca>();
        return app;
    }
}
