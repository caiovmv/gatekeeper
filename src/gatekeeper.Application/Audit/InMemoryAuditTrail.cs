using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using gatekeeper.Identity;

namespace gatekeeper.Audit;

public interface IAuditPublisher
{
    void Publish(AuditEntry entry);
}

public interface IAuditTrail
{
    AuditEntry PublishEntry(string actor, string action, string resource, string outcome, string correlation);

    bool Verify();

    void Tamper(int index, string outcome);

    IReadOnlyList<AuditEntry> Entries { get; }
}

public static class AuditActor
{
    public static string From(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "sistema";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()));
        return Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
    }
}

public sealed class InMemoryAuditTrail : IAuditTrail
{
    private readonly Chain _chain = new();
    private readonly IAuditPublisher _publisher;

    public InMemoryAuditTrail(IAuditPublisher publisher)
    {
        _publisher = publisher;
    }

    public IReadOnlyList<AuditEntry> Entries => _chain.Items;

    public AuditEntry PublishEntry(string actor, string action, string resource, string outcome, string correlation)
    {
        AuditEntry entry;
        lock (_chain.Gate)
        {
            entry = Build(actor, action, resource, outcome, correlation);
            _chain.Items.Add(entry);
            _chain.Last = entry.Hash;
        }

        _publisher.Publish(entry);
        return entry;
    }

    public bool Verify()
    {
        lock (_chain.Gate)
        {
            return Intact(_chain.Items);
        }
    }

    public void Tamper(int index, string outcome)
    {
        lock (_chain.Gate)
        {
            var current = _chain.Items[index];
            _chain.Items[index] = current with { Outcome = outcome };
        }
    }

    public static bool Intact(IReadOnlyList<AuditEntry> entries)
    {
        var previous = string.Empty;
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.PreviousHash, previous, StringComparison.Ordinal))
            {
                return false;
            }

            var calculated = HashOf(entry.OccurredAt, entry.Actor, entry.Action, entry.Resource, entry.Outcome, entry.Correlation, entry.PreviousHash);
            if (!string.Equals(calculated, entry.Hash, StringComparison.Ordinal))
            {
                return false;
            }

            previous = entry.Hash;
        }

        return true;
    }

    public static AuditEntry Compose(string actor, string action, string resource, string outcome, string correlation, string previous)
    {
        var occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var cleanActor = RedacaoDeSegredo.Aplicar(actor);
        var cleanAction = RedacaoDeSegredo.Aplicar(action);
        var cleanResource = RedacaoDeSegredo.Aplicar(resource);
        var cleanOutcome = RedacaoDeSegredo.Aplicar(outcome);
        var cleanCorrelation = RedacaoDeSegredo.Aplicar(correlation);
        var hash = HashOf(occurredAt, cleanActor, cleanAction, cleanResource, cleanOutcome, cleanCorrelation, previous);
        return new AuditEntry(occurredAt, cleanActor, cleanAction, cleanResource, cleanOutcome, cleanCorrelation, previous, hash);
    }

    private AuditEntry Build(string actor, string action, string resource, string outcome, string correlation)
    {
        return Compose(actor, action, resource, outcome, correlation, _chain.Last);
    }

    private static string HashOf(string occurredAt, string actor, string action, string resource, string outcome, string correlation, string previous)
    {
        var text = occurredAt + "|" + actor + "|" + action + "|" + resource + "|" + outcome + "|" + correlation + "|" + previous;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class Chain
    {
        public List<AuditEntry> Items { get; } = new();

        public string Last { get; set; } = string.Empty;

        public object Gate { get; } = new();
    }
}
