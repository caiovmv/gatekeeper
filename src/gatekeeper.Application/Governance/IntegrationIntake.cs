using System.Security.Cryptography;
using System.Text;

namespace gatekeeper.Governance;

public sealed class IntegrationIntake
{
    private readonly IntakeState _state = new();

    public string Register(Guid id, Guid tenantId)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        lock (_state.Gate)
        {
            _state.Bindings[id] = new Binding(tenantId, secret);
        }

        return secret;
    }

    public GovernanceOutcome Accept(Guid id, Guid tenantId, string sourceEventId, string payload, string signature)
    {
        lock (_state.Gate)
        {
            if (!_state.Bindings.TryGetValue(id, out var binding))
            {
                return new GovernanceOutcome(false, "quarantine");
            }

            if (!SignatureMatches(binding.Secret, payload, signature))
            {
                return new GovernanceOutcome(false, "signature rejected");
            }

            if (binding.TenantId != tenantId)
            {
                return new GovernanceOutcome(false, "tenant mismatch");
            }

            var key = id.ToString("N") + "|" + sourceEventId.Trim();
            if (!_state.Seen.Add(key))
            {
                return new GovernanceOutcome(false, "replay");
            }

            return new GovernanceOutcome(true, "accepted");
        }
    }

    private static bool SignatureMatches(string secret, string payload, string signature)
    {
        var presented = signature.Trim().ToLowerInvariant();
        if (presented.Length == 0)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(presented);
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private sealed class IntakeState
    {
        public object Gate { get; } = new();

        public Dictionary<Guid, Binding> Bindings { get; } = new();

        public HashSet<string> Seen { get; } = new();
    }

    private sealed record Binding(Guid TenantId, string Secret);
}
