using gatekeeper.Health;
using gatekeeper.Persistence.Adapters;

namespace gatekeeper.Health.Adapters;

public sealed class SondaDeBanco : ISondaDeInfraestrutura
{
    private readonly IFabricaDeConexao _fabrica;

    public SondaDeBanco(IFabricaDeConexao fabrica)
    {
        _fabrica = fabrica;
    }

    public string Nome => NomesDeSonda.Banco;

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        return Verificacao.Executar(AbrirAsync, cancellationToken);
    }

    private async Task AbrirAsync(CancellationToken cancellationToken)
    {
        await using var conexao = _fabrica.Criar();
        await conexao.OpenAsync(cancellationToken);
        await using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT 1";
        await comando.ExecuteScalarAsync(cancellationToken);
    }
}
