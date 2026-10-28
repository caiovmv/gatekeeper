using System.Collections.Concurrent;
using gatekeeper.Common;

namespace gatekeeper.Identity;

public sealed class LimitadorDeOrigem
{
    public const int TetoPadrao = 1000;

    private readonly int _teto;
    private readonly ConcurrentDictionary<string, int> _contagens = new();

    public LimitadorDeOrigem(int teto)
    {
        if (teto < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(teto));
        }

        _teto = teto;
    }

    public Resultado<int> Tentar(string origem)
    {
        if (string.IsNullOrWhiteSpace(origem))
        {
            return Resultado<int>.Falha(Erro.Validacao("origem vazia"));
        }

        var chave = origem.Trim();
        var atual = _contagens.AddOrUpdate(chave, 1, static (_, valor) => valor + 1);
        if (atual > _teto)
        {
            return Resultado<int>.Falha(Erro.Conflito("origem limitada"));
        }

        return Resultado<int>.Ok(atual);
    }
}
