using System.Collections.Concurrent;
using gatekeeper.Messaging;
using gatekeeper.Health;

namespace gatekeeper.Messaging.Adapters;

/// <summary>Usado quando Mensagens:Endpoint vem vazio.</summary>
public sealed class InMemoryMessageBroker : IMessageBroker, ISondaDeInfraestrutura
{
    private readonly string _nome;
    private readonly Caixa _caixa = new();

    public InMemoryMessageBroker(string nome)
    {
        _nome = nome;
    }

    public string Nome => _nome;

    public IReadOnlyList<QueueMessage> Publicadas
    {
        get
        {
            return _caixa.Publicadas.ToArray();
        }
    }

    public Task<MessageReceipt> PublicarAsync(QueueMessage mensagem, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirCorpo(mensagem);
        var id = Identificador(mensagem);
        if (_caixa.Publicadas.Exists(item => item.Id == id))
        {
            return Task.FromResult(new MessageReceipt(id));
        }

        var gravada = mensagem with { Id = id };
        _caixa.Publicadas.Add(gravada);
        _caixa.Pendentes.Enqueue(gravada);
        return Task.FromResult(new MessageReceipt(id));
    }

    public Task<MessageDelivery?> ReceberAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_caixa.Pendentes.TryDequeue(out var mensagem) || mensagem.Id is null)
        {
            return Task.FromResult<MessageDelivery?>(null);
        }

        _caixa.EmVoo[mensagem.Id] = mensagem;
        return Task.FromResult<MessageDelivery?>(new MessageDelivery(mensagem.Id, mensagem.Assunto, mensagem.Corpo));
    }

    public Task ConfirmarAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_caixa.EmVoo.TryRemove(id, out _))
        {
            throw new ArgumentException("Entrega '" + id + "' nao esta em aberto.", nameof(id));
        }

        return Task.CompletedTask;
    }

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EstadoDaSonda(true, "memoria"));
    }

    private static string Identificador(QueueMessage mensagem)
    {
        if (string.IsNullOrWhiteSpace(mensagem.Id))
        {
            return Guid.NewGuid().ToString("N");
        }

        return mensagem.Id.Trim();
    }

    private static void ExigirCorpo(QueueMessage mensagem)
    {
        if (string.IsNullOrWhiteSpace(mensagem.Corpo))
        {
            throw new ArgumentException("A mensagem precisa de corpo.", nameof(mensagem));
        }
    }

    private sealed class Caixa
    {
        public List<QueueMessage> Publicadas { get; } = new();

        public ConcurrentQueue<QueueMessage> Pendentes { get; } = new();

        public ConcurrentDictionary<string, QueueMessage> EmVoo { get; } = new();
    }
}
