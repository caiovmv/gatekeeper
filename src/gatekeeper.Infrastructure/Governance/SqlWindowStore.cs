using System.Globalization;
using gatekeeper.Persistence.Adapters;
using Dapper;

namespace gatekeeper.Governance;

public sealed class SqlWindowStore : IWindowStore
{
    private readonly SessaoSql _sessao;

    public SqlWindowStore(SessaoSql sessao)
    {
        _sessao = sessao;
    }

    public bool TryHit(string bucket, string key, int limit, DateTimeOffset start, DateTimeOffset now)
    {
        using var conexao = Abrir();
        using var transacao = conexao.BeginTransaction();
        conexao.Execute(
            "DELETE FROM process_mark WHERE bucket = @bucket AND mark_key = @key AND seen_on < @start",
            new { bucket, key, start = Texto(start) },
            transacao);
        var quantidade = conexao.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM process_mark WHERE bucket = @bucket AND mark_key = @key",
            new { bucket, key },
            transacao);
        if (quantidade >= limit)
        {
            transacao.Commit();
            return false;
        }

        conexao.Execute(
            "INSERT INTO process_mark (id, bucket, mark_key, seen_on) VALUES (@id, @bucket, @key, @seen)",
            new { id = Guid.NewGuid().ToString("D"), bucket, key, seen = Texto(now) },
            transacao);
        transacao.Commit();
        return true;
    }

    public bool Expired(string bucket, string key, DateTimeOffset now, TimeSpan idle)
    {
        using var conexao = Abrir();
        using var transacao = conexao.BeginTransaction();
        var visto = conexao.QuerySingleOrDefault<string>(
            "SELECT seen_on FROM process_mark WHERE bucket = @bucket AND mark_key = @key",
            new { bucket, key },
            transacao);
        if (string.IsNullOrEmpty(visto))
        {
            conexao.Execute(
                "INSERT INTO process_mark (id, bucket, mark_key, seen_on) VALUES (@id, @bucket, @key, @seen)",
                new { id = Guid.NewGuid().ToString("D"), bucket, key, seen = Texto(now) },
                transacao);
            transacao.Commit();
            return false;
        }

        var ultimo = DateTimeOffset.Parse(visto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (now - ultimo > idle)
        {
            conexao.Execute(
                "DELETE FROM process_mark WHERE bucket = @bucket AND mark_key = @key",
                new { bucket, key },
                transacao);
            transacao.Commit();
            return true;
        }

        conexao.Execute(
            "UPDATE process_mark SET seen_on = @seen WHERE bucket = @bucket AND mark_key = @key",
            new { bucket, key, seen = Texto(now) },
            transacao);
        transacao.Commit();
        return false;
    }

    public void Remove(string bucket, string key)
    {
        using var conexao = Abrir();
        conexao.Execute(
            "DELETE FROM process_mark WHERE bucket = @bucket AND mark_key = @key",
            new { bucket, key });
    }

    public void Remember(string bucket, string key)
    {
        if (Known(bucket, key))
        {
            return;
        }

        using var conexao = Abrir();
        conexao.Execute(
            "INSERT INTO process_mark (id, bucket, mark_key, seen_on) VALUES (@id, @bucket, @key, @seen)",
            new { id = Guid.NewGuid().ToString("D"), bucket, key, seen = Texto(DateTimeOffset.UnixEpoch) });
    }

    public bool Known(string bucket, string key)
    {
        using var conexao = Abrir();
        var quantidade = conexao.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM process_mark WHERE bucket = @bucket AND mark_key = @key",
            new { bucket, key });
        return quantidade > 0;
    }

    private System.Data.Common.DbConnection Abrir()
    {
        return _sessao.AbrirAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Texto(DateTimeOffset instante)
    {
        return instante.ToString("O", CultureInfo.InvariantCulture);
    }
}
