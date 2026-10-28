using gatekeeper.Messaging;

namespace gatekeeper.Messaging;

public interface IOutbox
{
    Task EnfileirarAsync(QueueMessage mensagem, CancellationToken cancellationToken);

    Task DespacharAsync(CancellationToken cancellationToken);
}
