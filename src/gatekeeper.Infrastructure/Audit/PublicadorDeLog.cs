using System.Globalization;
using gatekeeper.Audit;
using gatekeeper.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace gatekeeper.Audit.Adapters;

public sealed class PublicadorDeLog : IAccessLogPublisher
{
    private readonly IMessageBroker _broker;

    public PublicadorDeLog([FromKeyedServices(MessageRoles.Externo)] IMessageBroker broker)
    {
        _broker = broker;
    }

    public void Publish(AccessLog entry)
    {
        var body = "{\"occurredAt\":\"" + entry.OccurredAt.ToString("O", CultureInfo.InvariantCulture)
            + "\",\"correlation\":\"" + entry.Correlation
            + "\",\"subject\":\"" + entry.Subject
            + "\",\"organizationId\":\"" + entry.OrganizationId.ToString("D")
            + "\",\"method\":\"" + entry.Method
            + "\",\"path\":\"" + entry.Path
            + "\",\"status\":" + entry.Status.ToString(CultureInfo.InvariantCulture)
            + ",\"latencyMs\":" + entry.LatencyMs.ToString(CultureInfo.InvariantCulture)
            + "}";
        _broker.PublicarAsync(new QueueMessage("gatekeeper.logs", body), CancellationToken.None).GetAwaiter().GetResult();
    }
}
