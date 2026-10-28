namespace gatekeeper.Api;

public static class RegistroDeRotas
{
    public static WebApplication MapearRotas(this WebApplication app)
    {
        var modulos = app.Services.GetServices<IModuloDeRotas>();
        foreach (var modulo in modulos)
        {
            modulo.Mapear(app);
        }

        return app;
    }
}
