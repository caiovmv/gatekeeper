namespace gatekeeper.Common;

/// <summary>
/// Desfecho de um caso de uso. Falha esperada volta aqui. Excecao fica para o que quebrou de verdade.
/// </summary>
public sealed class Resultado<T>
{
    private readonly T? _valor;
    private readonly Erro? _erro;

    private Resultado(T? valor, Erro? erro)
    {
        _valor = valor;
        _erro = erro;
    }

    public bool Sucesso => _erro is null;

    public T Valor
    {
        get
        {
            if (_erro is not null)
            {
                throw new InvalidOperationException("Resultado de falha nao tem valor.");
            }

            return _valor!;
        }
    }

    public Erro Erro
    {
        get
        {
            if (_erro is null)
            {
                throw new InvalidOperationException("Resultado de sucesso nao tem erro.");
            }

            return _erro;
        }
    }

    public static Resultado<T> Ok(T valor)
    {
        return new Resultado<T>(valor, null);
    }

    public static Resultado<T> Falha(Erro erro)
    {
        return new Resultado<T>(default, erro);
    }
}
