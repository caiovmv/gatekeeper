namespace gatekeeper.Billing;

public sealed record Organization(Guid Id, string Name, string Host, string IdpIssuer, string DirectoryId = "", DateTimeOffset? RemovedOn = null);

public sealed record Member(Guid Id, Guid OrganizationId, string Email, string DisplayName, string Role, DateTimeOffset? RemovedOn = null);

public sealed record MachineKey(Guid Id, Guid OrganizationId, string Name, string Prefix, DateTimeOffset CreatedOn, DateTimeOffset? RevokedOn, DateTimeOffset? LastUsedOn);

public sealed record IssuedMachineKey(Guid Id, string Prefix, string Secret);

public static class PapelDeConsole
{
    public const string Admin = "admin";

    public const string Support = "support";

    public const string Auditor = "auditor";

    public const string Owner = "owner";

    public static string Canonico(string role)
    {
        var trimmed = role.Trim();
        if (string.Equals(trimmed, "Staff.Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, Admin, StringComparison.OrdinalIgnoreCase))
        {
            return Admin;
        }

        if (string.Equals(trimmed, "Staff.Support", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, Support, StringComparison.OrdinalIgnoreCase))
        {
            return Support;
        }

        if (string.Equals(trimmed, "Staff.Auditor", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, Auditor, StringComparison.OrdinalIgnoreCase))
        {
            return Auditor;
        }

        if (string.Equals(trimmed, Owner, StringComparison.OrdinalIgnoreCase))
        {
            return Owner;
        }

        if (trimmed.StartsWith("App.", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed.ToLowerInvariant();
    }

    public static bool Aceito(string role)
    {
        return role == Admin || role == Support || role == Auditor || role == Owner || role.StartsWith("App.", StringComparison.Ordinal);
    }
}
