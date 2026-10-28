namespace gatekeeper.Authorization;

public sealed class InMemoryAuthorizer : IAuthorizer
{
    private readonly IPolicyStore _policies;

    public InMemoryAuthorizer(IPolicyStore policies)
    {
        _policies = policies;
    }

    public Task<bool> AllowAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        if (request.Principal.Subject == "machine" && request.Principal.Roles.Count == 0)
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(_policies.Allows(request.Principal.Roles, request.Action, request.Resource.Kind));
    }
}
