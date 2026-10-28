using System.Data.Common;

namespace gatekeeper.Persistence.Adapters;

public sealed class FabricaDeConexao : IFabricaDeConexao
{
    private readonly string _provedor;
    private readonly string _connectionString;

    public FabricaDeConexao(OpcoesDeBanco opcoes)
    {
        _provedor = opcoes.ProvedorInvariante;
        _connectionString = opcoes.ConnectionString;
    }

    public DbConnection Criar()
    {
        DbProviderFactory fabrica;
        try
        {
            fabrica = DbProviderFactories.GetFactory(_provedor);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "Provedor '" + _provedor + "' nao esta registrado. "
                + "SqlServer, PostgreSQL, MySQL e SQLite entram na Api. "
                + "Outro driver precisa de DbProviderFactories.RegisterFactory no composition root.");
        }

        var conexao = fabrica.CreateConnection();
        if (conexao is null)
        {
            throw new InvalidOperationException("O provedor '" + _provedor + "' nao criou conexao.");
        }

        conexao.ConnectionString = _connectionString;
        return conexao;
    }
}
