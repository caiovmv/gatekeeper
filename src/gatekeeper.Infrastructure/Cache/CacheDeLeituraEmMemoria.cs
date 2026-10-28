using System.Collections.Concurrent;
using gatekeeper.Caching;

namespace gatekeeper.Caching.Adapters;

/// <summary>Indice em processo. Cada etiqueta aponta as chaves que uma escrita deve apagar.</summary>
public sealed class CacheDeLeituraEmMemoria : ICacheDeLeitura
{
    private readonly TimeProvider _relogio;
    private readonly ConcurrentDictionary<string, Item> _itens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _indice = new(StringComparer.Ordinal);

    public CacheDeLeituraEmMemoria(TimeProvider relogio)
    {
        _relogio = relogio;
    }

    public Task<LeituraGuardada?> ObterAsync(string chave, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirChave(chave);
        if (!_itens.TryGetValue(chave, out var item))
        {
            return Task.FromResult<LeituraGuardada?>(null);
        }

        if (item.Expira <= _relogio.GetUtcNow())
        {
            Apagar(chave, item);
            return Task.FromResult<LeituraGuardada?>(null);
        }

        return Task.FromResult<LeituraGuardada?>(item.Leitura);
    }

    public Task GravarAsync(string chave, LeituraGuardada leitura, IReadOnlyCollection<string> etiquetas, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirChave(chave);
        var lista = Normalizar(etiquetas);
        if (_itens.TryGetValue(chave, out var anterior))
        {
            Desindexar(chave, anterior.Etiquetas);
        }

        _itens[chave] = new Item(leitura, _relogio.GetUtcNow().Add(PoliticaDeCache.Validade), lista);
        foreach (var etiqueta in lista)
        {
            var conjunto = _indice.GetOrAdd(etiqueta, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            conjunto[chave] = 0;
        }

        return Task.CompletedTask;
    }

    public Task RemoverPorEtiquetasAsync(IReadOnlyCollection<string> etiquetas, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var etiqueta in Normalizar(etiquetas))
        {
            if (!_indice.TryRemove(etiqueta, out var conjunto))
            {
                continue;
            }

            foreach (var chave in conjunto.Keys)
            {
                if (_itens.TryRemove(chave, out var item))
                {
                    Desindexar(chave, item.Etiquetas);
                }
            }
        }

        return Task.CompletedTask;
    }

    private void Apagar(string chave, Item item)
    {
        _itens.TryRemove(chave, out _);
        Desindexar(chave, item.Etiquetas);
    }

    private void Desindexar(string chave, IReadOnlyList<string> etiquetas)
    {
        foreach (var etiqueta in etiquetas)
        {
            if (_indice.TryGetValue(etiqueta, out var conjunto))
            {
                conjunto.TryRemove(chave, out _);
            }
        }
    }

    private static string[] Normalizar(IReadOnlyCollection<string> etiquetas)
    {
        return etiquetas
            .Where(etiqueta => !string.IsNullOrWhiteSpace(etiqueta))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
        public Item(LeituraGuardada leitura, DateTimeOffset expira, IReadOnlyList<string> etiquetas)
        {
            Leitura = leitura;
            Expira = expira;
            Etiquetas = etiquetas;
        }

        public LeituraGuardada Leitura { get; }

        public DateTimeOffset Expira { get; }

        public IReadOnlyList<string> Etiquetas { get; }
    }
}
