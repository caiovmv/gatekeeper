using gatekeeper.Governance;

namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// PostgreSQL e qualquer outro que fale CREATE TABLE IF NOT EXISTS e OFFSET/FETCH.
/// </summary>
public sealed class DialetoAnsi : IDialetoSql
{
    public static readonly DialetoAnsi Instancia = new();

    private DialetoAnsi()
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

    public string SqlAlargarCaixa => SqlDeEsquema.AlargarCaixaAnsi;

    public string SqlGarantirGovernanca => GovernanceSchema.PostgreSql;

    public string SqlGarantirEscritorio => SqlDeEsquema.GarantirEscritorioAnsi;

    public string SqlGarantirOperacao => SqlDeEsquema.GarantirOperacaoAnsi;

    public string SqlAcordarPreco => SqlDeEsquema.AcordarPrecoAnsi;

    public string SqlGarantirEspacos => SqlDeEsquema.GarantirEspacosAnsi;

    public string Paginacao => "OFFSET @deslocamento ROWS FETCH NEXT @tamanho ROWS ONLY";
}
