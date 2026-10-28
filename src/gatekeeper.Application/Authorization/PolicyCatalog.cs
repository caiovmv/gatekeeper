using gatekeeper.Billing;
using gatekeeper.Common;

namespace gatekeeper.Authorization;

public sealed class PolicyDependencies
{
    public PolicyDependencies(IPolicyStore store, IPolicyPublisher publisher)
    {
        Store = store;
        Publisher = publisher;
    }

    public IPolicyStore Store { get; }

    public IPolicyPublisher Publisher { get; }
}

public sealed class PolicyCatalog
{
    private readonly IAuthorizer _authorizer;
    private readonly PolicyDependencies _dependencies;

    public PolicyCatalog(IAuthorizer authorizer, PolicyDependencies dependencies)
    {
        _authorizer = authorizer;
        _dependencies = dependencies;
    }

    public async Task<Resultado<IReadOnlyList<AccessRule>>> ListAsync(Principal principal, CancellationToken cancellationToken)
    {
        if (!await Allow(principal, "read", "policy", cancellationToken))
        {
            return Resultado<IReadOnlyList<AccessRule>>.Falha(Erro.NaoAutorizado("denied"));
        }

        return Resultado<IReadOnlyList<AccessRule>>.Ok(_dependencies.Store.Rules());
    }

    public async Task<Resultado<AccessRule>> SaveAsync(Principal principal, AccessRule rule, CancellationToken cancellationToken)
    {
        var role = PapelDeConsole.Canonico(rule.Role);
        var effect = rule.Effect.Trim().ToLowerInvariant();
        var valid = rule.Resource.Trim().Length > 0
            && rule.Action.Trim().Length > 0
            && PapelDeConsole.Aceito(role)
            && (effect == "allow" || effect == "deny");
        if (!valid)
        {
            return Resultado<AccessRule>.Falha(Erro.Validacao("policy is invalid"));
        }

        var action = _dependencies.Store.Rules().Any(item => item.Id == rule.Id) ? "update" : "create";
        if (!await Allow(principal, action, rule.Resource.Trim(), cancellationToken))
        {
            return Resultado<AccessRule>.Falha(Erro.NaoAutorizado("denied"));
        }

        var saved = rule with { Resource = rule.Resource.Trim(), Role = role, Action = rule.Action.Trim(), Effect = effect, RemovedOn = null };
        _dependencies.Store.Save(saved);
        await _dependencies.Publisher.PublishAsync(_dependencies.Store.History(), cancellationToken);
        return Resultado<AccessRule>.Ok(saved);
    }

    public async Task<Resultado<bool>> RemoveAsync(Principal principal, Guid id, CancellationToken cancellationToken)
    {
        if (!await Allow(principal, "delete", id.ToString(), cancellationToken))
        {
            return Resultado<bool>.Falha(Erro.NaoAutorizado("denied"));
        }

        _dependencies.Store.Remove(id, DateTimeOffset.UtcNow);
        await _dependencies.Publisher.PublishAsync(_dependencies.Store.History(), cancellationToken);
        return Resultado<bool>.Ok(true);
    }

    private Task<bool> Allow(Principal principal, string action, string id, CancellationToken cancellationToken)
    {
        return _authorizer.AllowAsync(new DecisionRequest(principal, action, new Resource("policy", id, principal.Organization)), cancellationToken);
    }
}
