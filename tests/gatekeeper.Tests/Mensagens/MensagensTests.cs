using gatekeeper.Messaging;
using gatekeeper.Health;
using gatekeeper.Messaging.Adapters;
using Xunit;

namespace gatekeeper.Tests.Mensagens;

public class MensagensTests
{
    [Fact]
    public async Task Memoria_guarda_a_mensagem_publicada()
    {
        var broker = new InMemoryMessageBroker(NomesDeSonda.MensagensInterno);
        await broker.PublicarAsync(new QueueMessage("eco", "oi"), CancellationToken.None);

        Assert.Equal("oi", broker.Publicadas[0].Corpo);
        var recibo = broker.Publicadas[0].Id;
        Assert.False(string.IsNullOrWhiteSpace(recibo));

        var deNovo = await broker.PublicarAsync(new QueueMessage("eco", "oi", recibo), CancellationToken.None);
        Assert.Equal(recibo, deNovo.Id);
        Assert.Single(broker.Publicadas);

        var entrega = await broker.ReceberAsync(CancellationToken.None);
        Assert.NotNull(entrega);
        Assert.Equal("oi", entrega.Corpo);
        await broker.ConfirmarAsync(entrega.Id, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => broker.ConfirmarAsync(entrega.Id, CancellationToken.None));
        var estado = await broker.VerificarAsync(CancellationToken.None);
        Assert.Equal("memoria", estado.Detalhe);
    }

    [Fact]
    public void Endpoint_sem_esquema_vira_nats_e_stream_invalido_falha()
    {
        var opcoes = DestinoDeMensagens.Montar(NomesDeSonda.MensagensInterno, "localhost:4222", null, null, null);
        Assert.Equal("nats://localhost:4222", opcoes.Endpoint);
        Assert.Equal("gatekeeper-interno", opcoes.Stream);
        Assert.Equal("gatekeeper.audit", opcoes.Subject);

        var externo = DestinoDeMensagens.Montar(NomesDeSonda.MensagensExterno, "tls://nats.externo:4222", "segredo", "fila_ext", "parceiro.eventos");
        Assert.Equal("tls://nats.externo:4222", externo.Endpoint);
        Assert.Equal("fila_ext", externo.Stream);
        Assert.Equal("segredo", externo.Token);

        Assert.Throws<ArgumentException>(() => DestinoDeMensagens.Montar(NomesDeSonda.MensagensInterno, "localhost:4222", null, "stream/evil", null));
        Assert.Throws<ArgumentException>(() => DestinoDeMensagens.Montar(NomesDeSonda.MensagensInterno, "localhost:4222", null, "ok", "assunto.*"));
    }

    [Fact]
    public void JetStream_nao_conecta_ao_ser_criado()
    {
        var opcoes = DestinoDeMensagens.Montar(NomesDeSonda.MensagensInterno, "127.0.0.1:1", null, "gatekeeper-interno", "gatekeeper.audit");
        var broker = new JetStreamBroker(opcoes);
        Assert.Equal(NomesDeSonda.MensagensInterno, broker.Nome);
        broker.Dispose();
    }
}
