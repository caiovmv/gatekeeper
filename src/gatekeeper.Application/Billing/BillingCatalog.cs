using gatekeeper.Authorization;
using gatekeeper.Common;

namespace gatekeeper.Billing;

public sealed class BillingCatalog
{
    private readonly IAuthorizer _authorizer;
    private readonly IBillingStore _store;

    public BillingCatalog(IAuthorizer authorizer, IBillingStore store)
    {
        _authorizer = authorizer;
        _store = store;
    }

    public Task<Resultado<IReadOnlyList<Organization>>> ListOrganizationsAsync(Principal principal, CancellationToken cancellationToken)
    {
        return ReadAsync(principal, "organization", () => _store.Organizations(), cancellationToken);
    }

    public Task<Resultado<Organization>> SaveOrganizationAsync(Principal principal, Organization organization, CancellationToken cancellationToken)
    {
        var valid = organization.Name.Trim().Length > 0 && organization.Host.Trim().Length > 0;
        return WriteAsync(principal, "organization", organization.Id, organization.Host, valid, () => _store.Save(organization), organization, cancellationToken);
    }

    public Task<Resultado<bool>> RemoveOrganizationAsync(Principal principal, Guid id, CancellationToken cancellationToken)
    {
        return RemoveAsync(principal, "organization", id, () => _store.RemoveOrganization(id), cancellationToken);
    }

    private async Task<Resultado<IReadOnlyList<T>>> ReadAsync<T>(Principal principal, string kind, Func<IReadOnlyList<T>> read, CancellationToken cancellationToken)
    {
        if (!await Allowed(principal, "read", kind, kind, cancellationToken))
        {
            return Resultado<IReadOnlyList<T>>.Falha(Erro.NaoAutorizado("denied"));
        }

        return Resultado<IReadOnlyList<T>>.Ok(read());
    }

    private async Task<Resultado<T>> WriteAsync<T>(Principal principal, string kind, Guid id, string label, bool valid, Action save, T value, CancellationToken cancellationToken)
    {
        if (!valid)
        {
            return Resultado<T>.Falha(Erro.Validacao(kind + " is invalid"));
        }

        var action = Exists(kind, id) ? "update" : "create";
        if (!await Allowed(principal, action, kind, label, cancellationToken))
        {
            return Resultado<T>.Falha(Erro.NaoAutorizado("denied"));
        }

        save();
        return Resultado<T>.Ok(value);
    }

    private async Task<Resultado<bool>> RemoveAsync(Principal principal, string kind, Guid id, Action remove, CancellationToken cancellationToken)
    {
        if (!await Allowed(principal, "delete", kind, id.ToString(), cancellationToken))
        {
            return Resultado<bool>.Falha(Erro.NaoAutorizado("denied"));
        }

        remove();
        return Resultado<bool>.Ok(true);
    }

    private bool Exists(string kind, Guid id)
    {
        return _store.Organizations().Any(item => item.Id == id);
    }

    private Task<bool> Allowed(Principal principal, string action, string kind, string id, CancellationToken cancellationToken)
    {
        return _authorizer.AllowAsync(
            new DecisionRequest(principal, action, new Resource(kind, id, principal.Organization)),
            cancellationToken);
    }
}
