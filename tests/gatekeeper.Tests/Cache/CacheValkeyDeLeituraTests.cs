using System.Net;
using System.Net.Http.Json;
using gatekeeper.Api;
using gatekeeper.Caching;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace gatekeeper.Tests.Cache;

public class CacheValkeyDeLeituraTests
{
    [Fact]
    public async Task Valkey_despeja_pela_etiqueta_e_o_prazo_fica_em_cinco_minutos()
    {
        await using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:ApiKey", "chave-um");
            builder.UseSetting("Caching:Prefix", "prova-valkey");
        });
        using var cliente = fabrica.CreateClient();

        using var motores = await Pegar(cliente, "/api/organizations");
        using var motoresDeNovo = await Pegar(cliente, "/api/organizations");
        using var membros = await Pegar(cliente, "/api/members?organizationId=" + Guid.Empty);
        using var membrosDeNovo = await Pegar(cliente, "/api/members?organizationId=" + Guid.Empty);
        var prazo = ValkeyDaSuite.Prazo("prova-valkey");
        var id = Guid.NewGuid();
        using var gravado = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Put, "/api/organizations/" + id)
        {
            Content = JsonContent.Create(new { name = "Valkey", host = "valkey.example", idpIssuer = "" }),
        });
        using var membrosDepois = await Pegar(cliente, "/api/members?organizationId=" + Guid.Empty);
        using var motoresDepois = await Pegar(cliente, "/api/organizations");

        Assert.Equal(CabecalhosDeCache.Falha, Estado(motores));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(motoresDeNovo));
        Assert.Equal(CabecalhosDeCache.Falha, Estado(membros));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(membrosDeNovo));
        Assert.InRange(prazo, 240, 300);
        Assert.Equal(HttpStatusCode.OK, gravado.StatusCode);
        Assert.Contains("organizations", gravado.Headers.GetValues(CabecalhosDeCache.Despejo).Single(), StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(membrosDepois));
        Assert.Contains("Valkey", await motoresDepois.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(motoresDepois));
    }

    private static async Task<HttpResponseMessage> Pegar(HttpClient cliente, string caminho)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
        pedido.Headers.Add("X-Api-Key", "chave-um");
        return await cliente.SendAsync(pedido);
    }

    private static async Task<HttpResponseMessage> Enviar(HttpClient cliente, HttpRequestMessage pedido)
    {
        pedido.Headers.Add("X-Api-Key", "chave-um");
        return await cliente.SendAsync(pedido);
    }

    private static string Estado(HttpResponseMessage resposta)
    {
        return resposta.Headers.GetValues(CabecalhosDeCache.Estado).Single();
    }
}
