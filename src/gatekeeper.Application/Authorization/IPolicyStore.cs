namespace gatekeeper.Authorization;

public interface IPolicyStore
{
    IReadOnlyList<AccessRule> Rules();

    IReadOnlyList<AccessRule> History();

    void Save(AccessRule rule);

    void Remove(Guid id, DateTimeOffset now);

    bool Allows(IReadOnlyList<string> roles, string action, string resource);
}

public interface IPolicyPublisher
{
    Task PublishAsync(IReadOnlyList<AccessRule> rules, CancellationToken cancellationToken);
}

public sealed class LocalPolicyPublisher : IPolicyPublisher
{
    public Task PublishAsync(IReadOnlyList<AccessRule> rules, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
