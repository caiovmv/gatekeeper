using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using gatekeeper.Messaging;
using gatekeeper.Health;
using gatekeeper.Health.Adapters;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace gatekeeper.Messaging.Adapters;

/// <summary>
/// JetStream. O mesmo protocolo serve o broker interno e o externo; o que muda e o servidor e o stream.
/// </summary>
public sealed class JetStreamBroker : IMessageBroker, ISondaDeInfraestrutura, IDisposable, IAsyncDisposable
{
    private readonly Canal _canal = new();
    private readonly OpcoesDeMensagens _opcoes;

    public JetStreamBroker(OpcoesDeMensagens opcoes)
    {
        _opcoes = opcoes;
    }

    public string Nome => _opcoes.Nome;

    public async Task<MessageReceipt> PublicarAsync(QueueMessage mensagem, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mensagem.Corpo))
        {
            throw new ArgumentException("A mensagem precisa de corpo.", nameof(mensagem));
        }

        var assunto = string.IsNullOrWhiteSpace(mensagem.Assunto) ? _opcoes.Subject : mensagem.Assunto.Trim();
        var id = string.IsNullOrWhiteSpace(mensagem.Id) ? Guid.NewGuid().ToString("N") : mensagem.Id.Trim();
        try
        {
            await _canal.PublicarAsync(_opcoes, new EnvelopeDeMensagem(assunto, mensagem.Corpo), id, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not ArgumentException and not OperationCanceledException)
        {
            throw new InvalidOperationException("JetStream nao confirmou " + _opcoes.Nome + ".", excecao);
        }

        return new MessageReceipt(id);
    }

    public Task<MessageDelivery?> ReceberAsync(CancellationToken cancellationToken)
    {
        return _canal.ReceberAsync(_opcoes, cancellationToken);
    }

    public Task ConfirmarAsync(string id, CancellationToken cancellationToken)
    {
        return _canal.ConfirmarAsync(id, cancellationToken);
    }

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        return Verificacao.Executar(token => _canal.VerificarAsync(_opcoes, token), cancellationToken);
    }

    public void Dispose()
    {
        _canal.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        return _canal.DisposeAsync();
    }

    private sealed class Canal : IAsyncDisposable
    {
        private static readonly NatsJsonContextSerializer<EnvelopeDeMensagem> _serializador = new(new JsonSerializerContext[] { ContextoDeEnvelope.Default });
        private readonly SemaphoreSlim _porta = new(1, 1);
        private readonly ConcurrentDictionary<string, INatsJSMsg<EnvelopeDeMensagem>> _emVoo = new();
        private NatsConnection? _cliente;
        private bool _streamPronto;

        public async Task PublicarAsync(OpcoesDeMensagens opcoes, EnvelopeDeMensagem envelope, string id, CancellationToken cancellationToken)
        {
            var cliente = await ObterAsync(opcoes, cancellationToken);
            var contexto = cliente.CreateJetStreamContext();
            await GarantirStreamAsync(contexto, opcoes, cancellationToken);
            var ack = await contexto.PublishAsync(
                opcoes.Subject,
                envelope,
                serializer: _serializador,
                opts: new NatsJSPubOpts { MsgId = id },
                cancellationToken: cancellationToken);
            ack.EnsureSuccess();
        }

        public async Task<MessageDelivery?> ReceberAsync(OpcoesDeMensagens opcoes, CancellationToken cancellationToken)
        {
            var cliente = await ObterAsync(opcoes, cancellationToken);
            var contexto = cliente.CreateJetStreamContext();
            await GarantirStreamAsync(contexto, opcoes, cancellationToken);
            var consumidor = await contexto.CreateOrUpdateConsumerAsync(
                opcoes.Stream,
                new ConsumerConfig(opcoes.Nome)
                {
                    FilterSubject = opcoes.Subject,
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                },
                cancellationToken);
            var opcoesDeLeitura = new NatsJSFetchOpts
            {
                MaxMsgs = 1,
                Expires = TimeSpan.FromMilliseconds(200),
            };
            await foreach (var mensagem in consumidor.FetchAsync<EnvelopeDeMensagem>(opcoesDeLeitura, serializer: _serializador, cancellationToken: cancellationToken))
            {
                if (mensagem.Data is null)
                {
                    await mensagem.AckAsync(cancellationToken: cancellationToken);
                    return null;
                }

                if (mensagem.Metadata is not { } metadados)
                {
                    await mensagem.AckAsync(cancellationToken: cancellationToken);
                    return null;
                }

                var id = metadados.Sequence.Stream.ToString();
                _emVoo[id] = mensagem;
                return new MessageDelivery(id, mensagem.Data.Assunto, mensagem.Data.Corpo);
            }

            return null;
        }

        public async Task ConfirmarAsync(string id, CancellationToken cancellationToken)
        {
            if (!_emVoo.TryRemove(id, out var mensagem))
            {
                throw new ArgumentException("Entrega '" + id + "' nao esta em aberto.", nameof(id));
            }

            await mensagem.AckAsync(cancellationToken: cancellationToken);
        }

        public async Task VerificarAsync(OpcoesDeMensagens opcoes, CancellationToken cancellationToken)
        {
            var cliente = await ObterAsync(opcoes, cancellationToken);
            await cliente.PingAsync(cancellationToken);
            var contexto = cliente.CreateJetStreamContext();
            try
            {
                await contexto.GetStreamAsync(opcoes.Stream, new StreamInfoRequest(), cancellationToken);
            }
            catch (NatsJSApiException excecao) when (excecao.Error is { Code: 404 } or { ErrCode: 10059 })
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_cliente is not null)
            {
                await _cliente.DisposeAsync();
            }

            _porta.Dispose();
        }

        private async Task<NatsConnection> ObterAsync(OpcoesDeMensagens opcoes, CancellationToken cancellationToken)
        {
            if (_cliente is not null)
            {
                return _cliente;
            }

            await _porta.WaitAsync(cancellationToken);
            try
            {
                if (_cliente is null)
                {
                    _cliente = new NatsConnection(CriarOpts(opcoes));
                }

                return _cliente;
            }
            finally
            {
                _porta.Release();
            }
        }

        private static NatsOpts CriarOpts(OpcoesDeMensagens opcoes)
        {
            var opts = new NatsOpts
            {
                Url = opcoes.Endpoint,
                Name = opcoes.Nome,
            };
            if (!string.IsNullOrWhiteSpace(opcoes.Token))
            {
                opts = opts with { AuthOpts = new NatsAuthOpts { Token = opcoes.Token } };
            }

            return opts;
        }

        private async Task GarantirStreamAsync(INatsJSContext contexto, OpcoesDeMensagens opcoes, CancellationToken cancellationToken)
        {
            if (_streamPronto)
            {
                return;
            }

            await contexto.CreateStreamAsync(
                new StreamConfig
                {
                    Name = opcoes.Stream,
                    Subjects = new[] { opcoes.Subject },
                },
                cancellationToken);
            _streamPronto = true;
        }
    }
}
