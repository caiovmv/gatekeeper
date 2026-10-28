using gatekeeper.Api;
using gatekeeper.Authorization;
using gatekeeper.Billing.Http;
using gatekeeper.Common;

namespace gatekeeper.Authorization.Http;

public sealed class PolicyRoutes : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/api/policies", List).WithTags("Authorization");
        rotas.MapPut("/api/policies/{id:guid}", Save).WithTags("Authorization");
        rotas.MapDelete("/api/policies/{id:guid}", Remove).WithTags("Authorization");
    }

    private static async Task<IResult> List(HttpContext context, PolicyCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.ListAsync(RequestPrincipal.Read(context), cancellationToken));
    }

    private static async Task<IResult> Save(Guid id, PolicyBody body, HttpContext context, PolicyCatalog catalog, CancellationToken cancellationToken)
    {
        var rule = new AccessRule(id, body.Resource ?? string.Empty, body.Role ?? string.Empty, body.Action ?? string.Empty, body.Effect ?? string.Empty, null);
        return Respond(await catalog.SaveAsync(RequestPrincipal.Read(context), rule, cancellationToken));
    }

    private static async Task<IResult> Remove(Guid id, HttpContext context, PolicyCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.RemoveAsync(RequestPrincipal.Read(context), id, cancellationToken));
    }

    private static IResult Respond<T>(Resultado<T> result)
    {
        if (!result.Sucesso && result.Erro.Codigo == CodigosDeErro.NaoAutorizado)
        {
            return Results.Json(RespostaHttp.ParaProblema(result.Erro), statusCode: StatusCodes.Status403Forbidden);
        }

        return RespostaHttp.De(result);
    }
}

public sealed record PolicyBody(string? Resource, string? Role, string? Action, string? Effect);
