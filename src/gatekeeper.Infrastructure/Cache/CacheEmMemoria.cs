using System.Collections.Concurrent;
using gatekeeper.Caching;
using gatekeeper.Health;

namespace gatekeeper.Caching.Adapters;

/// <summary>Usado quando Cache:ConnectionString vem vazio.</summary>
public sealed class InMemoryCache : ICache, ISondaDeInfraestrutura
{
    private readonly ConcurrentDictionary<string, Item> _itens = new();

    public Task<string?> ObterAsync(string chave, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirChave(chave);
        if (!_itens.TryGetValue(chave, out var item))
        {
            return Task.FromResult<string?>(null);
        }

        if (item.Expira <= DateTimeOffset.UtcNow)
        {
            _itens.TryRemove(chave, out _);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(item.Valor);
    }

    public Task GravarAsync(string chave, string valor, TimeSpan expiracao, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirChave(chave);
        if (expiracao <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiracao));
        }

        _itens[chave] = new Item(valor, DateTimeOffset.UtcNow.Add(expiracao));
        return Task.CompletedTask;
    }

    public Task RemoverAsync(string chave, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirChave(chave);
        _itens.TryRemove(chave, out _);
        return Task.CompletedTask;
    }

    public string Nome => NomesDeSonda.Cache;

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EstadoDaSonda(true, "memoria"));
    }

    private static void ExigirChave(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
        {
            throw new ArgumentException("Chave e obrigatoria.", nameof(chave));
        }
    }

    private sealed class Item
    {
        public Item(string valor, DateTimeOffset expira)
        {
            Valor = valor;
            Expira = expira;
        }

        public string Valor { get; }

        public DateTimeOffset Expira { get; }
    }
}
