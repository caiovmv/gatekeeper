namespace gatekeeper.Common;

/// <summary>
/// Um so lugar para o teto da pagina. O adaptador nao reinventa o limite.
/// </summary>
public static class Paginacao
{
    public const int TamanhoPadrao = 20;

    public const int TamanhoMaximo = 100;

    public static Recorte Normalizar(int pagina, int tamanho)
    {
        var paginaNormalizada = 1;
        if (pagina > 1)
        {
            paginaNormalizada = pagina;
        }

        var tamanhoNormalizado = TamanhoPadrao;
        if (tamanho > 0)
        {
            tamanhoNormalizado = tamanho;
        }

        if (tamanhoNormalizado > TamanhoMaximo)
        {
            tamanhoNormalizado = TamanhoMaximo;
        }

        return new Recorte(paginaNormalizada, tamanhoNormalizado);
    }
}
