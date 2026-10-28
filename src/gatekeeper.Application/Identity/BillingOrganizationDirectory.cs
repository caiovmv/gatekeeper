using gatekeeper.Billing;
using gatekeeper.Identity;

namespace gatekeeper.Identity;

public sealed class BillingOrganizationDirectory : IOrganizationDirectory
{
    private readonly IBillingStore _store;

    public BillingOrganizationDirectory(IBillingStore store)
    {
        _store = store;
    }

    public string? IssuerForHost(string host)
    {
        foreach (var organization in _store.Organizations())
        {
            if (string.Equals(organization.Host, host, StringComparison.OrdinalIgnoreCase) && organization.IdpIssuer.Length > 0)
            {
                return organization.IdpIssuer;
            }
        }

        return null;
    }
}
