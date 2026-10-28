using gatekeeper.Identity;

namespace gatekeeper.Health;

public sealed record EstadoDaSonda(bool Saudavel, string Detalhe)
{
    public static EstadoDaSonda Falha(Exception excecao)
    {
        var texto = RedacaoDeSegredo.Aplicar(excecao.Message);
        if (texto.Length > 160)
        {
            texto = texto.Substring(0, 160);
        }

        if (string.IsNullOrWhiteSpace(texto))
        {
            return new EstadoDaSonda(false, excecao.GetType().Name);
        }

        return new EstadoDaSonda(false, excecao.GetType().Name + ": " + texto);
    }
}
