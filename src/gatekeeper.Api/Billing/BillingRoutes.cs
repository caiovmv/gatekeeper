using gatekeeper.Api;
using gatekeeper.Audit;
using gatekeeper.Authorization;
using gatekeeper.Billing;
using gatekeeper.Common;
using gatekeeper.Identity;

namespace gatekeeper.Billing.Http;

public static class RequestPrincipal
{
    public static Principal Read(HttpContext context)
    {
        if (context.Items[AutenticacaoPorChave.PrincipalItem] is PlatformPrincipal principal)
        {
            return new Principal(principal.Subject, principal.Organization, principal.Roles, principal.Email);
        }

        return new Principal("machine", "local", Array.Empty<string>());
    }
}

public sealed class BillingRoutes : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/api/organizations", ListOrganizations).WithTags("Billing");
        rotas.MapPut("/api/organizations/{id:guid}", SaveOrganization).WithTags("Billing");
        rotas.MapDelete("/api/organizations/{id:guid}", RemoveOrganization).WithTags("Billing");
    }

    private static async Task<IResult> ListOrganizations(HttpContext context, BillingCatalog catalog, IOfficeStore store, CancellationToken cancellationToken)
    {
        var principal = RequestPrincipal.Read(context);
        var result = await catalog.ListOrganizationsAsync(principal, cancellationToken);
        if (!result.Sucesso || Atribuicao.Administra(principal))
        {
            return Respond(result);
        }

        var visible = new List<Organization>();
        foreach (var row in result.Valor)
        {
            if (Atribuicao.PodeLer(store, principal, row.Id))
            {
                visible.Add(row);
            }
        }

        return Results.Ok(visible);
    }

    private static async Task<IResult> SaveOrganization(Guid id, OrganizationBody body, HttpContext context, BillingCatalog catalog, IAuditTrail audit, CancellationToken cancellationToken)
    {
        var organization = new Organization(id, (body.Name ?? string.Empty).Trim(), (body.Host ?? string.Empty).Trim(), (body.IdpIssuer ?? string.Empty).Trim(), (body.DirectoryId ?? string.Empty).Trim());
        return Noted(context, audit, "organization", id.ToString(), await catalog.SaveOrganizationAsync(RequestPrincipal.Read(context), organization, cancellationToken));
    }

    private static async Task<IResult> RemoveOrganization(Guid id, HttpContext context, BillingCatalog catalog, IAuditTrail audit, CancellationToken cancellationToken)
    {
        return Noted(context, audit, "organization", id.ToString(), await catalog.RemoveOrganizationAsync(RequestPrincipal.Read(context), id, cancellationToken));
    }

    private static IResult Noted<T>(HttpContext context, IAuditTrail audit, string kind, string id, Resultado<T> result)
    {
        var principal = RequestPrincipal.Read(context);
        var outcome = result.Sucesso ? "allow" : result.Erro.Codigo;
        audit.PublishEntry(principal.Subject, kind, id, outcome, context.TraceIdentifier);
        return Respond(result);
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

public sealed record OrganizationBody(string Name, string Host, string IdpIssuer, string? DirectoryId);

internal static class Atribuicao
{
    public static bool Administra(Principal principal)
    {
        foreach (var role in principal.Roles)
        {
            if (role == "admin" || role == "Staff.Admin")
            {
                return true;
            }
        }

        return false;
    }

    public static bool PodeLer(IOfficeStore store, Principal principal, Guid organizationId)
    {
        if (Administra(principal) || (principal.Subject == "machine" && principal.Email.Trim().Length == 0))
        {
            return true;
        }

        var email = principal.Email.Trim();
        if (email.Length == 0)
        {
            return false;
        }

        foreach (var member in store.Members())
        {
            if (member.RemovedOn is null && member.OrganizationId == organizationId && string.Equals(member.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
