using gatekeeper.Common;

namespace gatekeeper.Api;

/// <summary>
/// Unico mapa de resultado para HTTP. Controller e rota usam isto, nao repetem o status.
/// </summary>
public static class RespostaHttp
{
    public static int StatusDe(Erro erro)
    {
        if (erro.Codigo == CodigosDeErro.NaoEncontrado)
        {
            return StatusCodes.Status404NotFound;
        }

        if (erro.Codigo == CodigosDeErro.Conflito)
        {
            return StatusCodes.Status409Conflict;
        }

        if (erro.Codigo == CodigosDeErro.Inesperado)
        {
            return StatusCodes.Status500InternalServerError;
        }

        return StatusCodes.Status422UnprocessableEntity;
    }

    public static Problema ParaProblema(Erro erro)
    {
        return new Problema(erro.Codigo, erro.Mensagem);
    }

    public static IResult De<T>(Resultado<T> resultado)
    {
        return De(resultado, StatusCodes.Status200OK);
    }

    public static IResult De<T>(Resultado<T> resultado, int statusDeSucesso)
    {
        if (resultado.Sucesso)
        {
            return Results.Json(resultado.Valor, statusCode: statusDeSucesso);
        }

        var erro = resultado.Erro;
        return Results.Json(ParaProblema(erro), statusCode: StatusDe(erro));
    }
}
