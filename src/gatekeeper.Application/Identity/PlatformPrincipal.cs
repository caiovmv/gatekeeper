namespace gatekeeper.Identity;

public sealed record PlatformPrincipal(string Subject, string Organization, string Issuer, IReadOnlyList<string> Roles, string Email = "");
