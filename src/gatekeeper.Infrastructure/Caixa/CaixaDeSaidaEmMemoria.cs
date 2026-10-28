using System.Collections.Concurrent;
using gatekeeper.Messaging;

namespace gatekeeper.Messaging.Adapters;

public sealed class InMemoryOutbox : IOutbox
{
    private readonly IMessageBroker _broker;
    private readonly ConcurrentQueue<QueueMessage> _pendentes = new();

    public InMemoryOutbox(IMessageBroker broker)
    {
        _broker = broker;
    }

    public Task EnfileirarAsync(QueueMessage mensagem, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = string.IsNullOrWhiteSpace(mensagem.Id) ? Guid.NewGuid().ToString("N") : mensagem.Id.Trim();
        _pendentes.Enqueue(mensagem with { Id = id });
        return Task.CompletedTask;
    }

    public async Task DespacharAsync(CancellationToken cancellationToken)
    {
        while (_pendentes.TryDequeue(out var mensagem))
        {
            await _broker.PublicarAsync(mensagem, cancellationToken);
        }
    }
}
