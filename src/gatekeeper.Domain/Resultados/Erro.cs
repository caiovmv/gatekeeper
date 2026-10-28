namespace gatekeeper.Common;

/// <summary>
/// Falha esperada. Nasce por fabrica para o codigo nao virar string solta em cada servico.
/// </summary>
public sealed class Erro
{
    private Erro(string codigo, string mensagem)
    {
        Codigo = codigo;
        Mensagem = mensagem;
    }

    public string Codigo { get; }

    public string Mensagem { get; }

    public static Erro NaoEncontrado(string recurso)
    {
        return new Erro(CodigosDeErro.NaoEncontrado, recurso + " nao encontrado.");
    }

    public static Erro Validacao(string mensagem)
    {
        return new Erro(CodigosDeErro.Validacao, mensagem);
    }

    public static Erro Conflito(string mensagem)
    {
        return new Erro(CodigosDeErro.Conflito, mensagem);
    }

    public static Erro Inesperado()
    {
        return new Erro(CodigosDeErro.Inesperado, "Falha interna.");
    }

    public static Erro NaoAutorizado(string mensagem)
    {
        return new Erro(CodigosDeErro.NaoAutorizado, mensagem);
    }
}
