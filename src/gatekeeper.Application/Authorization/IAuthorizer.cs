namespace gatekeeper.Authorization;

public sealed record Principal(string Subject, string Organization, IReadOnlyList<string> Roles, string Email = "");

public sealed record Resource(string Kind, string Id, string Organization);

public sealed record DecisionRequest(Principal Principal, string Action, Resource Resource);

public interface IAuthorizer
{
    Task<bool> AllowAsync(DecisionRequest request, CancellationToken cancellationToken);
}
