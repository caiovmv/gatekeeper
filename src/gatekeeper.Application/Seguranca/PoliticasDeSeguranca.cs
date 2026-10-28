using System.Text.RegularExpressions;
using gatekeeper.Audit;
using gatekeeper.Common;

namespace gatekeeper.Identity;

public readonly record struct DecisaoDeGravacao(Resultado<int> Decisao, AuditEntry Registro);

public static class PoliticaDeGravacao
{
    public static DecisaoDeGravacao Autorizar(
        bool consentida,
        int retencaoDias,
        string finalidade,
        IAuditTrail trilha,
        string correlacao)
    {
        if (string.Equals(finalidade, "autenticacao", StringComparison.Ordinal))
        {
            var recusa = trilha.PublishEntry("sistema", "gravacao.recusada", "voz", "finalidade autenticacao negada", correlacao);
            return new DecisaoDeGravacao(Resultado<int>.Falha(Erro.NaoAutorizado("finalidade autenticacao negada")), recusa);
        }

        if (!consentida)
        {
            var recusa = trilha.PublishEntry("sistema", "gravacao.recusada", "voz", "sem consentimento", correlacao);
            return new DecisaoDeGravacao(Resultado<int>.Falha(Erro.NaoAutorizado("gravacao sem consentimento")), recusa);
        }

        if (retencaoDias < 1)
        {
            var recusa = trilha.PublishEntry("sistema", "gravacao.recusada", "voz", "retencao invalida", correlacao);
            return new DecisaoDeGravacao(Resultado<int>.Falha(Erro.Validacao("retencao invalida")), recusa);
        }

        var aceite = trilha.PublishEntry("sistema", "gravacao.autorizada", "voz", retencaoDias.ToString(), correlacao);
        return new DecisaoDeGravacao(Resultado<int>.Ok(retencaoDias), aceite);
    }
}

public static class MascaraDeDocumento
{
    public static string Aplicar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var cnpj = Regex.Replace(texto, @"\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}", "**.***.***/****-**");
        return Regex.Replace(cnpj, @"\d{3}\.\d{3}\.\d{3}-\d{2}", "***.***.***-**");
    }
}
