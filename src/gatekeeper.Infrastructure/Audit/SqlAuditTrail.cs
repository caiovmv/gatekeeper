using gatekeeper.Persistence.Adapters;
using Dapper;

namespace gatekeeper.Audit;

public sealed class SqlAuditTrail : IAuditTrail
{
    private static readonly object _porta = new();
    private readonly SessaoSql _sessao;
    private readonly IAuditPublisher _publisher;

    public SqlAuditTrail(SessaoSql sessao, IAuditPublisher publisher)
    {
        _sessao = sessao;
        _publisher = publisher;
    }

    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            return Ler();
        }
    }

    public AuditEntry PublishEntry(string actor, string action, string resource, string outcome, string correlation)
    {
        AuditEntry entrada;
        lock (_porta)
        {
            using var conexao = Abrir();
            using var transacao = conexao.BeginTransaction();
            var anterior = conexao.QuerySingleOrDefault<string>(
                "SELECT hash FROM audit_chain WHERE seq = (SELECT MAX(seq) FROM audit_chain)",
                transaction: transacao) ?? string.Empty;
            entrada = InMemoryAuditTrail.Compose(actor, action, resource, outcome, correlation, anterior);
            var seq = conexao.ExecuteScalar<long>("SELECT COALESCE(MAX(seq), 0) FROM audit_chain", transaction: transacao) + 1;
            conexao.Execute(
                "INSERT INTO audit_chain (seq, occurred_at, actor, action, resource, outcome, correlation, previous_hash, hash) VALUES (@seq, @occurred, @actor, @action, @resource, @outcome, @correlation, @previous, @hash)",
                new
                {
                    seq,
                    occurred = entrada.OccurredAt,
                    actor = entrada.Actor,
                    action = entrada.Action,
                    resource = entrada.Resource,
                    outcome = entrada.Outcome,
                    correlation = entrada.Correlation,
                    previous = entrada.PreviousHash,
                    hash = entrada.Hash,
                },
                transacao);
            transacao.Commit();
        }

        _publisher.Publish(entrada);
        return entrada;
    }

    public bool Verify()
    {
        return InMemoryAuditTrail.Intact(Ler());
    }

    public void Tamper(int index, string outcome)
    {
        var linhas = Ler();
        if (index < 0 || index >= linhas.Count)
        {
            return;
        }

        using var conexao = Abrir();
        var seqs = conexao.Query<long>("SELECT seq FROM audit_chain ORDER BY seq").ToList();
        conexao.Execute(
            "UPDATE audit_chain SET outcome = @outcome WHERE seq = @seq",
            new { outcome, seq = seqs[index] });
    }

    private List<AuditEntry> Ler()
    {
        using var conexao = Abrir();
        var linhas = conexao.Query<Linha>(
            "SELECT occurred_at AS OccurredAt, actor AS Actor, action AS Action, resource AS Resource, outcome AS Outcome, correlation AS Correlation, previous_hash AS PreviousHash, hash AS Hash FROM audit_chain ORDER BY seq");
        var entradas = new List<AuditEntry>();
        foreach (var linha in linhas)
        {
            entradas.Add(new AuditEntry(linha.OccurredAt, linha.Actor, linha.Action, linha.Resource, linha.Outcome, linha.Correlation, linha.PreviousHash, linha.Hash));
        }

        return entradas;
    }

    private System.Data.Common.DbConnection Abrir()
    {
        return _sessao.AbrirAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed class Linha
    {
        public string OccurredAt { get; set; } = string.Empty;

        public string Actor { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string Resource { get; set; } = string.Empty;

        public string Outcome { get; set; } = string.Empty;

        public string Correlation { get; set; } = string.Empty;

        public string PreviousHash { get; set; } = string.Empty;

        public string Hash { get; set; } = string.Empty;
    }
}
