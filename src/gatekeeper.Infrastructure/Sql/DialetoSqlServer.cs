using gatekeeper.Governance;

namespace gatekeeper.Persistence.Adapters;

public sealed class DialetoSqlServer : IDialetoSql
{
    public static readonly DialetoSqlServer Instancia = new();

    private DialetoSqlServer()
    {
    }

    public string SqlGarantirVersao => SqlDeEsquema.GarantirVersaoSqlServer;

    public string SqlLerVersao => SqlDeEsquema.LerVersao;

    public string SqlGravarVersao => SqlDeEsquema.GravarVersao;

    public string SqlGarantirTabela =>
        """
        IF OBJECT_ID(N'mensagem', N'U') IS NULL
        BEGIN
            CREATE TABLE mensagem (
                id char(36) NOT NULL PRIMARY KEY,
                texto nvarchar(4000) NOT NULL
            );
        END
        """;

    public string SqlGarantirCaixa => SqlDeEsquema.GarantirCaixaSqlServer;

    public string SqlAlargarCaixa => SqlDeEsquema.AlargarCaixaSqlServer;

    public string SqlGarantirGovernanca => GovernanceSchema.Portable;

    public string SqlGarantirEscritorio => SqlDeEsquema.GarantirEscritorioSqlServer;

    public string SqlGarantirOperacao => SqlDeEsquema.GarantirOperacaoSqlServer;

    public string SqlAcordarPreco => SqlDeEsquema.AcordarPrecoSqlServer;

    public string SqlGarantirEspacos => SqlDeEsquema.GarantirEspacosSqlServer;

    public string Paginacao => "OFFSET @deslocamento ROWS FETCH NEXT @tamanho ROWS ONLY";
}
