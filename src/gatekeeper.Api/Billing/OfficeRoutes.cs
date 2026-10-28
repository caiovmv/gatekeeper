using gatekeeper.Api;
using gatekeeper.Audit;
using gatekeeper.Authorization;
using gatekeeper.Billing;
using gatekeeper.Common;
using gatekeeper.Health;
using gatekeeper.Identity;

namespace gatekeeper.Billing.Http;

public sealed class OfficeRoutes : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/api/members", ListMembers).WithTags("Office");
        rotas.MapPut("/api/members/{id:guid}", SaveMember).WithTags("Office");
        rotas.MapDelete("/api/members/{id:guid}", RemoveMember).WithTags("Office");
        rotas.MapGet("/api/machine-keys", ListMachineKeys).WithTags("Office");
        rotas.MapPost("/api/machine-keys", IssueMachineKey).WithTags("Office");
        rotas.MapPut("/api/machine-keys/{id:guid}", RenameMachineKey).WithTags("Office");
        rotas.MapDelete("/api/machine-keys/{id:guid}", RevokeMachineKey).WithTags("Office");
        rotas.MapGet("/api/audit", ListAudit).WithTags("Office");
        rotas.MapGet("/api/access-logs", ListLogs).WithTags("Office");
        rotas.MapGet("/api/platform/health", Health).WithTags("Office");
    }

    private static async Task<IResult> ListMembers(Guid organizationId, HttpContext context, OfficeCatalog catalog, IOfficeStore store, CancellationToken cancellationToken)
    {
        if (!Atribuicao.PodeLer(store, RequestPrincipal.Read(context), organizationId))
        {
            return Results.Json(new { status = 403, detail = "denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Respond(await catalog.ListMembersAsync(RequestPrincipal.Read(context), organizationId, cancellationToken));
    }

    private static async Task<IResult> SaveMember(Guid id, MemberBody body, HttpContext context, OfficeCatalog catalog, IAccountDirectory accounts, CancellationToken cancellationToken)
    {
        var member = new Member(id, body.OrganizationId, body.Email ?? string.Empty, body.DisplayName ?? string.Empty, body.Role ?? string.Empty);
        var saved = await catalog.SaveMemberAsync(RequestPrincipal.Read(context), member, cancellationToken);
        if (!saved.Sucesso)
        {
            return Respond(saved);
        }

        var provision = await accounts.ProvisionAsync(saved.Valor.Email, saved.Valor.DisplayName, saved.Valor.Role, saved.Valor.OrganizationId, body.Password ?? string.Empty, cancellationToken);
        if (provision.Status == "invalid")
        {
            await catalog.RemoveMemberAsync(RequestPrincipal.Read(context), id, cancellationToken);
            return Results.Json(RespostaHttp.ParaProblema(Erro.Validacao(provision.Detail)), statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(new { member = saved.Valor, inviteCode = provision.Hint });
    }

    private static async Task<IResult> RemoveMember(Guid id, HttpContext context, OfficeCatalog catalog, IOfficeStore store, IAccountDirectory accounts, CancellationToken cancellationToken)
    {
        var email = string.Empty;
        foreach (var member in store.Members())
        {
            if (member.Id == id)
            {
                email = member.Email;
            }
        }

        var removed = await catalog.RemoveMemberAsync(RequestPrincipal.Read(context), id, cancellationToken);
        if (removed.Sucesso && email.Length > 0)
        {
            await accounts.RetireAsync(email, cancellationToken);
        }

        return Respond(removed);
    }

    private static async Task<IResult> ListMachineKeys(Guid? organizationId, HttpContext context, OfficeCatalog catalog, IOfficeStore store, CancellationToken cancellationToken)
    {
        var id = organizationId ?? Guid.Empty;
        if (organizationId is Guid real && !Atribuicao.PodeLer(store, RequestPrincipal.Read(context), real))
        {
            return Results.Json(new { status = 403, detail = "denied" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Respond(await catalog.ListMachineKeysAsync(RequestPrincipal.Read(context), id, cancellationToken));
    }

    private static async Task<IResult> IssueMachineKey(MachineKeyBody body, HttpContext context, OfficeCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.IssueMachineKeyAsync(RequestPrincipal.Read(context), Guid.NewGuid(), body.OrganizationId, body.Name ?? string.Empty, cancellationToken));
    }

    private static async Task<IResult> RenameMachineKey(Guid id, MachineKeyName body, HttpContext context, OfficeCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.RenameMachineKeyAsync(RequestPrincipal.Read(context), id, body.Name ?? string.Empty, cancellationToken));
    }

    private static async Task<IResult> RevokeMachineKey(Guid id, HttpContext context, OfficeCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.RevokeMachineKeyAsync(RequestPrincipal.Read(context), id, cancellationToken));
    }

    private static async Task<IResult> ListAudit(HttpContext context, OfficeCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.ListAuditAsync(RequestPrincipal.Read(context), cancellationToken));
    }

    private static async Task<IResult> ListLogs(HttpContext context, IngestCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.ListLogsAsync(RequestPrincipal.Read(context), cancellationToken));
    }

    private static async Task<IResult> Health(IEnumerable<ISondaDeInfraestrutura> probes, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var report = await PlatformHealth.ReadAsync(probes, configuration, cancellationToken);
        return Results.Ok(report);
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

public sealed record MemberBody(Guid OrganizationId, string Email, string DisplayName, string Role, string? Password);

public sealed record MachineKeyBody(Guid OrganizationId, string Name);

public sealed record MachineKeyName(string? Name);
