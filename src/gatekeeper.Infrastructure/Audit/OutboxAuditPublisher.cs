using gatekeeper.Audit;
using gatekeeper.Messaging;

namespace gatekeeper.Audit.Adapters;

public sealed class OutboxAuditPublisher : IAuditPublisher
{
    private readonly IOutbox _outbox;

    public OutboxAuditPublisher(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public void Publish(AuditEntry entry)
    {
        var body = "{\"occurredAt\":\"" + entry.OccurredAt
            + "\",\"actor\":\"" + entry.Actor
            + "\",\"action\":\"" + entry.Action
            + "\",\"resource\":\"" + entry.Resource
            + "\",\"outcome\":\"" + entry.Outcome
            + "\",\"correlation\":\"" + entry.Correlation
            + "\",\"previousHash\":\"" + entry.PreviousHash
            + "\",\"hash\":\"" + entry.Hash
            + "\"}";
        _outbox.EnfileirarAsync(new QueueMessage("gatekeeper.audit", body, entry.Hash), CancellationToken.None).GetAwaiter().GetResult();
    }
}
