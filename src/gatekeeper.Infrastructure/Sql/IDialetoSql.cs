namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// So o que o SQL ansi nao cobre: DDL de criacao e a clausula de pagina.
/// O repositorio nao ramifica por nome de banco.
/// </summary>
public interface IDialetoSql
{
    string SqlGarantirVersao { get; }

    string SqlLerVersao { get; }

    string SqlGravarVersao { get; }

    string SqlGarantirTabela { get; }

    string SqlGarantirCaixa { get; }

    string SqlAlargarCaixa { get; }

    string SqlGarantirGovernanca { get; }

    string SqlGarantirEscritorio { get; }

    string SqlGarantirOperacao { get; }

    string SqlAcordarPreco { get; }

    string SqlGarantirEspacos { get; }

    string Paginacao { get; }
}
