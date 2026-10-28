using gatekeeper.Messaging;
using gatekeeper.Persistence.Adapters;
using Dapper;

namespace gatekeeper.Messaging.Adapters;

public sealed class SqlOutbox : IOutbox
{
    private readonly SessaoSql _sessao;
    private readonly IMessageBroker _broker;

    public SqlOutbox(SessaoSql sessao, IMessageBroker broker)
    {
        _sessao = sessao;
        _broker = broker;
    }

    public async Task EnfileirarAsync(QueueMessage mensagem, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(mensagem.Id) ? Guid.NewGuid().ToString("N") : mensagem.Id.Trim();
        await using var conexao = await _sessao.AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            "INSERT INTO caixa_saida (id, assunto, corpo, publicada) VALUES (@id, @assunto, @corpo, 0)",
            new
            {
                id,
                assunto = mensagem.Assunto ?? string.Empty,
                corpo = mensagem.Corpo,
            },
            cancellationToken: cancellationToken));
    }

    public async Task DespacharAsync(CancellationToken cancellationToken)
    {
        await using var conexao = await _sessao.AbrirAsync(cancellationToken);
        var linhas = await conexao.QueryAsync<Linha>(new CommandDefinition(
            "SELECT id AS Id, assunto AS Assunto, corpo AS Corpo FROM caixa_saida WHERE publicada = 0",
            cancellationToken: cancellationToken));
        foreach (var linha in linhas)
        {
            await _broker.PublicarAsync(new QueueMessage(linha.Assunto, linha.Corpo, linha.Id), cancellationToken);
            await conexao.ExecuteAsync(new CommandDefinition(
                "UPDATE caixa_saida SET publicada = 1 WHERE id = @id",
                new { id = linha.Id },
                cancellationToken: cancellationToken));
        }
    }

    private sealed class Linha
    {
        public string Id { get; set; } = string.Empty;

        public string Assunto { get; set; } = string.Empty;

        public string Corpo { get; set; } = string.Empty;
    }
}
