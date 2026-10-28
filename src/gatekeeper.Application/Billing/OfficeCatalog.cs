using gatekeeper.Audit;
using gatekeeper.Authorization;
using gatekeeper.Common;

namespace gatekeeper.Billing;

public sealed class OfficeDependencies
{
    public OfficeDependencies(IOfficeStore store, IBillingStore billing, IAuditTrail audit)
    {
        Store = store;
        Billing = billing;
        Audit = audit;
    }

    public IOfficeStore Store { get; }

    public IBillingStore Billing { get; }

    public IAuditTrail Audit { get; }
}

public sealed class OfficeCatalog
{
    private readonly IAuthorizer _authorizer;
    private readonly OfficeDependencies _dependencies;

    public OfficeCatalog(IAuthorizer authorizer, OfficeDependencies dependencies)
    {
        _authorizer = authorizer;
        _dependencies = dependencies;
    }

    public Task<Resultado<IReadOnlyList<Member>>> ListMembersAsync(Principal principal, Guid organizationId, CancellationToken cancellationToken)
    {
        return ListAsync(principal, "member", () => (IReadOnlyList<Member>)_dependencies.Store.Members().Where(item => item.OrganizationId == organizationId).ToArray(), cancellationToken);
    }

    public Task<Resultado<Member>> SaveMemberAsync(Principal principal, Member member, CancellationToken cancellationToken)
    {
        var role = CanonicalRole(member.Role);
        var valid = member.Email.Contains('@') && member.DisplayName.Trim().Length > 0 && AcceptedRole(role) && OrganizationExists(member.OrganizationId);
        var saved = member with { Email = member.Email.Trim(), DisplayName = member.DisplayName.Trim(), Role = role };
        return WriteAsync(principal, "member", saved.Id, saved.Email, valid, MemberAction(saved.Id), () =>
        {
            _dependencies.Store.Save(saved);
            return saved;
        }, cancellationToken);
    }

    public Task<Resultado<bool>> RemoveMemberAsync(Principal principal, Guid id, CancellationToken cancellationToken)
    {
        return RemoveAsync(principal, "member", id, () => _dependencies.Store.RemoveMember(id), cancellationToken);
    }

    public Task<Resultado<IReadOnlyList<MachineKey>>> ListMachineKeysAsync(Principal principal, Guid organizationId, CancellationToken cancellationToken)
    {
        return ListAsync(principal, "machine-key", () => (IReadOnlyList<MachineKey>)_dependencies.Store.MachineKeys().Where(item => item.OrganizationId == organizationId).ToArray(), cancellationToken);
    }

    public Task<Resultado<IssuedMachineKey>> IssueMachineKeyAsync(Principal principal, Guid id, Guid organizationId, string name, CancellationToken cancellationToken)
    {
        var valid = name.Trim().Length > 0 && OrganizationExists(organizationId);
        return WriteAsync(
            principal,
            "machine-key",
            id,
            name.Trim(),
            valid,
            "create",
            () => _dependencies.Store.Issue(id, organizationId, name, DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public Task<Resultado<MachineKey>> RenameMachineKeyAsync(Principal principal, Guid id, string name, CancellationToken cancellationToken)
    {
        var current = _dependencies.Store.MachineKeys().FirstOrDefault(item => item.Id == id && item.RevokedOn is null);
        var valid = current is not null && name.Trim().Length > 0;
        return WriteAsync(principal, "machine-key", id, name.Trim(), valid, "update", () =>
        {
            _dependencies.Store.Rename(id, name);
            return current! with { Name = name.Trim() };
        }, cancellationToken);
    }

    public Task<Resultado<bool>> RevokeMachineKeyAsync(Principal principal, Guid id, CancellationToken cancellationToken)
    {
        return RemoveAsync(principal, "machine-key", id, () => _dependencies.Store.Revoke(id, DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task<Resultado<IReadOnlyList<AuditEntry>>> ListAuditAsync(Principal principal, CancellationToken cancellationToken)
    {
        if (!await Allowed(principal, "read", "audit", "audit", cancellationToken))
        {
            Note(principal, "audit.read", "audit", "deny");
            return Resultado<IReadOnlyList<AuditEntry>>.Falha(Erro.NaoAutorizado("denied"));
        }

        Note(principal, "audit.read", "audit", "allow");
        return Resultado<IReadOnlyList<AuditEntry>>.Ok(_dependencies.Audit.Entries);
    }

    private bool OrganizationExists(Guid organizationId)
    {
        foreach (var organization in _dependencies.Billing.Organizations())
        {
            if (organization.Id == organizationId)
            {
                return true;
            }
        }

        return false;
    }

    private static string CanonicalRole(string role)
    {
        return PapelDeConsole.Canonico(role);
    }

    private static bool AcceptedRole(string role)
    {
        return PapelDeConsole.Aceito(role);
    }

    private static bool Exists<T>(Guid id, IReadOnlyList<T> items, Func<T, Guid> key)
    {
        foreach (var item in items)
        {
            if (key(item) == id)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Resultado<T>> ListAsync<T>(Principal principal, string kind, Func<T> read, CancellationToken cancellationToken)
    {
        if (!await Allowed(principal, "read", kind, kind, cancellationToken))
        {
            Note(principal, kind + ".read", kind, "deny");
            return Resultado<T>.Falha(Erro.NaoAutorizado("denied"));
        }

        return Resultado<T>.Ok(read());
    }

    private string MemberAction(Guid id)
    {
        foreach (var member in _dependencies.Store.Members())
        {
            if (member.Id == id)
            {
                return "update";
            }
        }

        return "create";
    }

    private async Task<Resultado<T>> WriteAsync<T>(Principal principal, string kind, Guid id, string label, bool valid, string action, Func<T> produce, CancellationToken cancellationToken)
    {
        if (!valid)
        {
            Note(principal, kind + ".rejected", label, CodigosDeErro.Validacao);
            return Resultado<T>.Falha(Erro.Validacao(kind + " is invalid"));
        }

        if (!await Allowed(principal, action, kind, label, cancellationToken))
        {
            Note(principal, kind + "." + action, label, "deny");
            return Resultado<T>.Falha(Erro.NaoAutorizado("denied"));
        }

        var value = produce();
        Note(principal, kind + "." + action, label, "allow");
        return Resultado<T>.Ok(value);
    }

    private async Task<Resultado<bool>> RemoveAsync(Principal principal, string kind, Guid id, Action remove, CancellationToken cancellationToken)
    {
        if (!await Allowed(principal, "delete", kind, id.ToString(), cancellationToken))
        {
            Note(principal, kind + ".delete", id.ToString(), "deny");
            return Resultado<bool>.Falha(Erro.NaoAutorizado("denied"));
        }

        remove();
        Note(principal, kind + ".delete", id.ToString(), "allow");
        return Resultado<bool>.Ok(true);
    }

    private void Note(Principal principal, string action, string resource, string outcome)
    {
        _dependencies.Audit.PublishEntry(principal.Subject, action, resource, outcome, principal.Organization);
    }

    private Task<bool> Allowed(Principal principal, string action, string kind, string id, CancellationToken cancellationToken)
    {
        return _authorizer.AllowAsync(new DecisionRequest(principal, action, new Resource(kind, id, principal.Organization)), cancellationToken);
    }
}
