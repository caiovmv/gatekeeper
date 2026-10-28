using System.Xml.Linq;

namespace gatekeeper.Governance;

public sealed record OidcLogin(
    string Code,
    string State,
    string ExpectedState,
    string Nonce,
    string ExpectedNonce,
    string Issuer,
    string ExpectedIssuer,
    string Audience,
    string ExpectedAudience,
    string Method,
    DateTimeOffset ExpiresAt,
    DateTimeOffset Now,
    bool StaffIssuer,
    string IdentityType);

public static class OidcChecks
{
    public static GovernanceOutcome Validate(OidcLogin login)
    {
        if (login.Code.Length == 0 || login.State.Length == 0 || login.Nonce.Length == 0 || login.Method != "S256")
        {
            return new GovernanceOutcome(false, "oidc incomplete");
        }

        if (!string.Equals(login.State, login.ExpectedState, StringComparison.Ordinal) || !string.Equals(login.Nonce, login.ExpectedNonce, StringComparison.Ordinal))
        {
            return new GovernanceOutcome(false, "transaction mismatch");
        }

        if (!string.Equals(login.Issuer, login.ExpectedIssuer, StringComparison.Ordinal) || !string.Equals(login.Audience, login.ExpectedAudience, StringComparison.Ordinal))
        {
            return new GovernanceOutcome(false, "issuer mismatch");
        }

        if (login.ExpiresAt <= login.Now)
        {
            return new GovernanceOutcome(false, "expired");
        }

        if (login.StaffIssuer && login.IdentityType == "user")
        {
            return new GovernanceOutcome(false, "tenant identity cannot become staff");
        }

        return new GovernanceOutcome(true, "accepted");
    }
}

public static class SamlChecks
{
    public static GovernanceOutcome Validate(string xml, string expectedAudience, string expectedInResponseTo, string expectedDestination, string expectedRecipient, string expectedCertificate)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return new GovernanceOutcome(false, "saml rejected");
        }

        var assertions = document.Descendants().Where(element => element.Name.LocalName == "Assertion").ToList();
        if (assertions.Count != 1)
        {
            return new GovernanceOutcome(false, "wrapping");
        }

        var assertion = assertions[0];
        var signature = assertion.Elements().FirstOrDefault(element => element.Name.LocalName == "Signature");
        if (signature is null)
        {
            return new GovernanceOutcome(false, "signature missing");
        }

        var id = assertion.Attribute("ID")?.Value ?? string.Empty;
        var reference = signature.Descendants().FirstOrDefault(element => element.Name.LocalName == "Reference")?.Attribute("URI")?.Value ?? string.Empty;
        if (id.Length == 0 || reference != "#" + id)
        {
            return new GovernanceOutcome(false, "wrapping");
        }

        var certificate = signature.Descendants().FirstOrDefault(element => element.Name.LocalName == "X509Certificate")?.Value?.Trim() ?? string.Empty;
        if (!string.Equals(certificate, expectedCertificate.Trim(), StringComparison.Ordinal))
        {
            return new GovernanceOutcome(false, "certificate mismatch");
        }

        var audience = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Audience")?.Value ?? string.Empty;
        var inResponseTo = document.Root?.Attribute("InResponseTo")?.Value ?? string.Empty;
        var destination = document.Root?.Attribute("Destination")?.Value ?? string.Empty;
        var recipient = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "SubjectConfirmationData")?.Attribute("Recipient")?.Value ?? string.Empty;
        if (audience != expectedAudience || inResponseTo != expectedInResponseTo || destination != expectedDestination || recipient != expectedRecipient)
        {
            return new GovernanceOutcome(false, "saml mismatch");
        }

        return new GovernanceOutcome(true, "accepted");
    }
}

public interface IWindowStore
{
    bool TryHit(string bucket, string key, int limit, DateTimeOffset start, DateTimeOffset now);

    bool Expired(string bucket, string key, DateTimeOffset now, TimeSpan idle);

    void Remove(string bucket, string key);

    void Remember(string bucket, string key);

    bool Known(string bucket, string key);
}

public sealed class MemoryWindowStore : IWindowStore
{
    private readonly Dictionary<string, List<DateTimeOffset>> _hits = new();
    private readonly Dictionary<string, DateTimeOffset> _seen = new();
    private readonly HashSet<string> _marks = new();

    public bool TryHit(string bucket, string key, int limit, DateTimeOffset start, DateTimeOffset now)
    {
        var name = Nome(bucket, key);
        lock (_hits)
        {
            if (!_hits.TryGetValue(name, out var hits))
            {
                hits = new List<DateTimeOffset>();
                _hits[name] = hits;
            }

            hits.RemoveAll(hit => hit < start);
            if (hits.Count >= limit)
            {
                return false;
            }

            hits.Add(now);
            return true;
        }
    }

    public bool Expired(string bucket, string key, DateTimeOffset now, TimeSpan idle)
    {
        var name = Nome(bucket, key);
        lock (_hits)
        {
            if (!_seen.TryGetValue(name, out var last))
            {
                _seen[name] = now;
                return false;
            }

            if (now - last > idle)
            {
                _seen.Remove(name);
                return true;
            }

            _seen[name] = now;
            return false;
        }
    }

    public void Remove(string bucket, string key)
    {
        var name = Nome(bucket, key);
        lock (_hits)
        {
            _seen.Remove(name);
            _hits.Remove(name);
            _marks.Remove(name);
        }
    }

    public void Remember(string bucket, string key)
    {
        lock (_hits)
        {
            _marks.Add(Nome(bucket, key));
        }
    }

    public bool Known(string bucket, string key)
    {
        lock (_hits)
        {
            return _marks.Contains(Nome(bucket, key));
        }
    }

    private static string Nome(string bucket, string key)
    {
        return bucket + "|" + key;
    }
}

public sealed class MutationBudget
{
    private readonly IWindowStore _store;

    public MutationBudget()
        : this(new MemoryWindowStore())
    {
    }

    public MutationBudget(IWindowStore store)
    {
        _store = store;
    }

    public bool Allow(string bucket, string key, int limit, TimeSpan window, DateTimeOffset now)
    {
        return _store.TryHit(bucket, key, limit, now - window, now);
    }
}

public sealed class LoginThrottle
{
    private readonly IWindowStore _store;

    public LoginThrottle()
        : this(new MemoryWindowStore())
    {
    }

    public LoginThrottle(IWindowStore store)
    {
        _store = store;
    }

    public bool Allow(string ip, string identity, DateTimeOffset now)
    {
        var key = ip.Trim() + "|" + identity.Trim().ToLowerInvariant();
        return _store.TryHit("login", key, 10, now.AddMinutes(-5), now);
    }
}

public sealed class SessionClock
{
    private readonly IWindowStore _store;

    public SessionClock()
        : this(new MemoryWindowStore())
    {
    }

    public SessionClock(IWindowStore store)
    {
        _store = store;
    }

    public bool Expired(string subject, DateTimeOffset now)
    {
        return _store.Expired("session", subject, now, TimeSpan.FromMinutes(30));
    }

    public void Forget(string subject)
    {
        _store.Remove("session", subject);
    }

    public void RevokeToken(string token)
    {
        if (token.Trim().Length == 0)
        {
            return;
        }

        _store.Remember("revoked", Hash(token.Trim()));
    }

    public bool TokenRevoked(string token)
    {
        if (token.Trim().Length == 0)
        {
            return false;
        }

        return _store.Known("revoked", Hash(token.Trim()));
    }

    private static string Hash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
