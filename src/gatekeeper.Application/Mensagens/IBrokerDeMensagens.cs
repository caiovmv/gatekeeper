namespace gatekeeper.Messaging;

/// <summary>
/// Durable publish with a read acknowledgement. Internal and external brokers share this port.
/// </summary>
public interface IMessageBroker
{
    Task<MessageReceipt> PublicarAsync(QueueMessage mensagem, CancellationToken cancellationToken);

    Task<MessageDelivery?> ReceberAsync(CancellationToken cancellationToken);

    Task ConfirmarAsync(string id, CancellationToken cancellationToken);
}
