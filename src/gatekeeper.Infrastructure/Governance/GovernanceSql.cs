using System.Data.Common;
using gatekeeper.Persistence.Adapters;
using Dapper;

namespace gatekeeper.Governance.Adapters;

/// <summary>
/// Mutação, auditoria e outbox na mesma transação. Falha de qualquer inserção desfaz as três.
/// </summary>
public sealed class GovernanceSql
{
    private readonly SessaoSql _sessao;

    public GovernanceSql(SessaoSql sessao)
    {
        _sessao = sessao;
    }

    public async Task<bool> CommitOrganizationAsync(Guid organizationId, string name, Guid eventId, CancellationToken cancellationToken)
    {
        await using var conexao = await _sessao.AbrirAsync(cancellationToken);
        await using var transacao = await conexao.BeginTransactionAsync(cancellationToken);
        try
        {
            await conexao.ExecuteAsync(new CommandDefinition(
                "INSERT INTO organization (id, name, status, version) VALUES (@id, @name, 'active', 1)",
                new { id = organizationId.ToString("D"), name },
                transacao,
                cancellationToken: cancellationToken));
            await conexao.ExecuteAsync(new CommandDefinition(
                "INSERT INTO audit_event (id, actor_id, action, created_at) VALUES (@id, @actor, 'create', @created)",
                new { id = eventId.ToString("D"), actor = organizationId.ToString("D"), created = DateTimeOffset.UtcNow.ToString("O") },
                transacao,
                cancellationToken: cancellationToken));
            await conexao.ExecuteAsync(new CommandDefinition(
                "INSERT INTO caixa_saida (id, assunto, corpo, publicada) VALUES (@id, 'governance', @corpo, 0)",
                new { id = eventId.ToString("D"), corpo = organizationId.ToString("D") },
                transacao,
                cancellationToken: cancellationToken));
            await transacao.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbException)
        {
            await transacao.RollbackAsync(cancellationToken);
            GovernanceMeters.OutboxPublishFailures.Add(1);
            return false;
        }
    }

    public async Task<int> CountAsync(string table, CancellationToken cancellationToken)
    {
        if (table != "organization" && table != "audit_event" && table != "caixa_saida")
        {
            throw new InvalidOperationException("Tabela fora do contrato de governança.");
        }

        await using var conexao = await _sessao.AbrirAsync(cancellationToken);
        return await conexao.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM " + table,
            cancellationToken: cancellationToken));
    }
}
