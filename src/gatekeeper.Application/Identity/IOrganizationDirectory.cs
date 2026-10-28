namespace gatekeeper.Identity;

public interface IOrganizationDirectory
{
    string? IssuerForHost(string host);
}

public sealed class DefaultOrganizationDirectory : IOrganizationDirectory
{
    public string? IssuerForHost(string host)
    {
        return null;
    }
}
