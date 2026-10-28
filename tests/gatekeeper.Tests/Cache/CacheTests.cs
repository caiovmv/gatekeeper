using gatekeeper.Caching.Adapters;
using Xunit;

namespace gatekeeper.Tests.Cache;

public class CacheTests
{
    [Theory]
    [InlineData("Redis", ProvedoresDeCache.Redis)]
    [InlineData("Valkey", ProvedoresDeCache.Valkey)]
    public void Redis_e_valkey_sao_os_provedores_de_cache(string bruto, string canonico)
    {
        Assert.Equal(canonico, ProvedoresDeCache.Canonico(bruto));
    }

    [Fact]
    public void Provedor_desconhecido_nao_entra_calado()
    {
        Assert.Throws<ArgumentException>(() => ProvedoresDeCache.Canonico("Memcached"));
    }

    [Fact]
    public async Task Memoria_grava_le_e_expira()
    {
        var cache = new InMemoryCache();
        await cache.GravarAsync("sessao", "abc", TimeSpan.FromMilliseconds(40), CancellationToken.None);
        Assert.Equal("abc", await cache.ObterAsync("sessao", CancellationToken.None));

        await Task.Delay(80);
        Assert.Null(await cache.ObterAsync("sessao", CancellationToken.None));
    }
}
