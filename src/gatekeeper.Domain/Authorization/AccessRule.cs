namespace gatekeeper.Authorization;

public sealed record AccessRule(Guid Id, string Resource, string Role, string Action, string Effect, DateTimeOffset? RemovedOn);
