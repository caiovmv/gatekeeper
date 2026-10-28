using System.Data.Common;

namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// Abre a conexao. O migrador aplica o esquema na primeira abertura de cada banco.
/// </summary>
public sealed class SessaoSql
{
    private readonly IDialetoSql _dialeto;
    private readonly MigradorSql _migrador;

    public SessaoSql(IFabricaDeConexao fabrica, IDialetoSql dialeto)
    {
        _dialeto = dialeto;
        _migrador = new MigradorSql(fabrica);
    }

    public string Paginacao => _dialeto.Paginacao;

    public bool EhViolacaoDeChave(DbException excecao)
    {
        return ConflitoDeChave.EhViolacao(excecao);
    }

    public Task<DbConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        return _migrador.AbrirAsync(_dialeto, cancellationToken);
    }
}
