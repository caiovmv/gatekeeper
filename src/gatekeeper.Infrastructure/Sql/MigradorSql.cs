using System.Collections.Concurrent;
using System.Data.Common;
using Dapper;

namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// Aplica cada versao uma vez por banco. As aberturas seguintes nao repetem o DDL.
/// </summary>
public sealed class MigradorSql
{
    private readonly IFabricaDeConexao _fabrica;
    private readonly ConcurrentDictionary<string, byte> _prontos = new();

    public MigradorSql(IFabricaDeConexao fabrica)
    {
        _fabrica = fabrica;
    }

    public async Task<DbConnection> AbrirAsync(IDialetoSql dialeto, CancellationToken cancellationToken)
    {
        var conexao = _fabrica.Criar();
        try
        {
            await conexao.OpenAsync(cancellationToken);
            var chave = conexao.ConnectionString ?? string.Empty;
            if (!_prontos.ContainsKey(chave))
            {
                await AplicarAsync(conexao, dialeto, cancellationToken);
                _prontos.TryAdd(chave, 0);
            }

            return conexao;
        }
        catch
        {
            await conexao.DisposeAsync();
            throw;
        }
    }

    private static async Task AplicarAsync(DbConnection conexao, IDialetoSql dialeto, CancellationToken cancellationToken)
    {
        await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirVersao, cancellationToken: cancellationToken));
        var aplicadas = new HashSet<int>();
        var versoes = await conexao.QueryAsync<int>(new CommandDefinition(dialeto.SqlLerVersao, cancellationToken: cancellationToken));
        foreach (var versao in versoes)
        {
            aplicadas.Add(versao);
        }

        if (!aplicadas.Contains(1))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirTabela, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 1, cancellationToken);
        }

        if (!aplicadas.Contains(2))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirCaixa, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 2, cancellationToken);
        }

        if (!aplicadas.Contains(3))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirGovernanca, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 3, cancellationToken);
        }

        if (!aplicadas.Contains(4))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlAlargarCaixa, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 4, cancellationToken);
        }

        if (!aplicadas.Contains(5))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirEscritorio, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 5, cancellationToken);
        }

        if (!aplicadas.Contains(6))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirOperacao, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 6, cancellationToken);
        }

        if (!aplicadas.Contains(7))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlAcordarPreco, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 7, cancellationToken);
        }

        if (!aplicadas.Contains(9))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirEspacos, cancellationToken: cancellationToken));
            await GravarAsync(conexao, dialeto, 9, cancellationToken);
        }
    }

    private static Task GravarAsync(DbConnection conexao, IDialetoSql dialeto, int versao, CancellationToken cancellationToken)
    {
        return conexao.ExecuteAsync(new CommandDefinition(
            dialeto.SqlGravarVersao,
            new { versao },
            cancellationToken: cancellationToken));
    }
}
