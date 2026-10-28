using gatekeeper.Governance;

namespace gatekeeper.Persistence.Adapters;

public sealed class DialetoSqlite : IDialetoSql
{
    public static readonly DialetoSqlite Instancia = new();

    private DialetoSqlite()
    {
    }

    public string SqlGarantirVersao => SqlDeEsquema.GarantirVersaoAnsi;

    public string SqlLerVersao => SqlDeEsquema.LerVersao;

    public string SqlGravarVersao => SqlDeEsquema.GravarVersao;

    public string SqlGarantirTabela =>
        """
        CREATE TABLE IF NOT EXISTS mensagem (
            id char(36) NOT NULL PRIMARY KEY,
            texto varchar(4000) NOT NULL
        )
        """;

    public string SqlGarantirCaixa => SqlDeEsquema.GarantirCaixaAnsi;

    public string SqlAlargarCaixa => "SELECT 1";

    public string SqlGarantirGovernanca => GovernanceSchema.Portable;

    public string SqlGarantirEscritorio => SqlDeEsquema.GarantirEscritorioAnsi;

    public string SqlGarantirOperacao => SqlDeEsquema.GarantirOperacaoAnsi;

    public string SqlAcordarPreco => SqlDeEsquema.AcordarPrecoAnsi;

    public string SqlGarantirEspacos => SqlDeEsquema.GarantirEspacosAnsi;

    public string Paginacao => "LIMIT @tamanho OFFSET @deslocamento";
}
