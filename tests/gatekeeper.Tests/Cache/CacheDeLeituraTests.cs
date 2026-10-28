using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using gatekeeper.Api;
using gatekeeper.Caching;
using gatekeeper.Caching.Adapters;
using gatekeeper.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gatekeeper.Tests.Cache;

public class CacheDeLeituraTests
{
    private const string Chave = "chave-um";

    [Fact]
    public async Task Memoria_expira_no_prazo_e_despeja_pela_etiqueta()
    {
        var relogio = new Relogio(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var cache = new CacheDeLeituraEmMemoria(relogio);
        var leitura = new LeituraGuardada(200, "application/json", "[]", "organizations");
        await cache.GravarAsync("organizacoes", leitura, new[] { "organizations" }, CancellationToken.None);
        await cache.GravarAsync("trilha", leitura with { Etiquetas = "audit" }, new[] { "audit" }, CancellationToken.None);

        await cache.RemoverPorEtiquetasAsync(new[] { "organizations" }, CancellationToken.None);

        Assert.Null(await cache.ObterAsync("organizacoes", CancellationToken.None));
        Assert.NotNull(await cache.ObterAsync("trilha", CancellationToken.None));

        relogio.Agora = relogio.Agora.Add(PoliticaDeCache.Validade);
        Assert.Null(await cache.ObterAsync("trilha", CancellationToken.None));
    }

    [Fact]
    public async Task Segundo_get_repete_e_a_escrita_invalida_preserva()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();

        using var primeiro = await Pegar(cliente, "/api/organizations");
        using var segundo = await Pegar(cliente, "/api/organizations");
        using var recusa = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Put, "/api/organizations/" + Guid.NewGuid())
        {
            Content = JsonContent.Create(new { name = "", host = "", idpIssuer = "" }),
        });
        using var terceiro = await Pegar(cliente, "/api/organizations");

        Assert.Equal(CabecalhosDeCache.Falha, Estado(primeiro));
        Assert.Equal("organizations", primeiro.Headers.GetValues(CabecalhosDeCache.Etiquetas).Single());
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(segundo));
        Assert.Equal("no-store", segundo.Headers.GetValues("Cache-Control").Single());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusa.StatusCode);
        Assert.False(recusa.Headers.Contains(CabecalhosDeCache.Despejo));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(terceiro));
    }

    [Fact]
    public async Task Put_e_delete_despejam_e_o_proximo_get_ve_o_dado_novo()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var id = Guid.NewGuid();

        using var criado = await Enviar(cliente, OrganizacaoNome(id, "Alpha"));
        using var miss = await Pegar(cliente, "/api/organizations");
        using var hit = await Pegar(cliente, "/api/organizations");
        using var atualizado = await Enviar(cliente, OrganizacaoNome(id, "Beta"));
        using var depois = await Pegar(cliente, "/api/organizations");
        using var removido = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Delete, "/api/organizations/" + id));
        using var vazio = await Pegar(cliente, "/api/organizations");

        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        Assert.Contains("organizations", criado.Headers.GetValues(CabecalhosDeCache.Despejo).Single(), StringComparison.Ordinal);
        Assert.Contains("audit", criado.Headers.GetValues(CabecalhosDeCache.Despejo).Single(), StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(miss));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(hit));
        Assert.Contains("Beta", await depois.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(depois));
        Assert.Equal(HttpStatusCode.OK, removido.StatusCode);
        Assert.DoesNotContain(id.ToString(), await vazio.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(vazio));
    }

    [Fact]
    public async Task Post_de_chamada_despeja_calls_usage_e_audit()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var organizacao = Guid.NewGuid();
        using var gravada = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Put, "/api/organizations/" + organizacao)
        {
            Content = JsonContent.Create(new { name = "Local", host = "cache.example", idpIssuer = "https://idp.example" }),
        });
        using var trilha = await Pegar(cliente, "/api/audit");
        using var trilhaDeNovo = await Pegar(cliente, "/api/audit");
        using var chamada = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Post, "/api/ingest/audit")
        {
            Content = JsonContent.Create(new
            {
                actor = "ada",
                action = "member.read",
                resource = "member",
                outcome = "allow",
                correlation = "cache-1",
            }),
        });
        using var trilhaDepois = await Pegar(cliente, "/api/audit");

        Assert.Equal(HttpStatusCode.OK, gravada.StatusCode);
        Assert.Contains("members", gravada.Headers.GetValues(CabecalhosDeCache.Despejo).Single(), StringComparison.Ordinal);
        Assert.Contains("machine-keys", gravada.Headers.GetValues(CabecalhosDeCache.Despejo).Single(), StringComparison.Ordinal);
        Assert.Contains("audit", gravada.Headers.GetValues(CabecalhosDeCache.Despejo).Single(), StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(trilha));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(trilhaDeNovo));
        Assert.Equal(HttpStatusCode.OK, chamada.StatusCode);
        var despejo = chamada.Headers.GetValues(CabecalhosDeCache.Despejo).Single();
        Assert.Contains("access-log", despejo, StringComparison.Ordinal);
        Assert.Contains("audit", despejo, StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(trilhaDepois));
    }

    [Fact]
    public async Task Prazo_de_cinco_minutos_fica_no_valkey()
    {
        await using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:ApiKey", Chave);
            builder.UseSetting("Caching:Prefix", "prova-prazo");
        });
        using var cliente = fabrica.CreateClient();

        using var primeiro = await Pegar(cliente, "/api/organizations");
        using var segundo = await Pegar(cliente, "/api/organizations");
        var prazo = ValkeyDaSuite.Prazo("prova-prazo");

        Assert.Equal(CabecalhosDeCache.Falha, Estado(primeiro));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(segundo));
        Assert.Equal("no-store", segundo.Headers.GetValues("Cache-Control").Single());
        Assert.InRange(prazo, 240, 300);
    }

    [Fact]
    public async Task Principais_e_chaves_nao_compartilham_entrada()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var organizacao = Guid.NewGuid();
        using var criada = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Put, "/api/organizations/" + organizacao)
        {
            Content = JsonContent.Create(new { name = "Local", host = "chaves.example", idpIssuer = "https://idp.example" }),
        });
        using var emitida = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Post, "/api/machine-keys")
        {
            Content = JsonContent.Create(new { organizationId = organizacao, name = "segunda" }),
        });
        var segredo = (await emitida.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString();

        using var ana = await cliente.SendAsync(Bearer("/api/organizations", Sessao("ana", "org-a")));
        using var bia = await cliente.SendAsync(Bearer("/api/organizations", Sessao("bia", "org-b")));
        using var anaDeNovo = await cliente.SendAsync(Bearer("/api/organizations", Sessao("ana", "org-a")));
        using var chaveUm = await Pegar(cliente, "/api/organizations");
        using var chaveUmDeNovo = await Pegar(cliente, "/api/organizations");
        using var chaveDois = await cliente.SendAsync(ComChave("/api/organizations", segredo!));
        using var chaveDoisDeNovo = await cliente.SendAsync(ComChave("/api/organizations", segredo!));

        Assert.Equal(HttpStatusCode.OK, criada.StatusCode);
        Assert.Equal(HttpStatusCode.OK, emitida.StatusCode);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(ana));
        Assert.Equal(CabecalhosDeCache.Falha, Estado(bia));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(anaDeNovo));
        Assert.Equal(CabecalhosDeCache.Falha, Estado(chaveUm));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(chaveUmDeNovo));
        Assert.Equal(CabecalhosDeCache.Falha, Estado(chaveDois));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(chaveDoisDeNovo));
    }

    [Fact]
    public async Task Login_saude_e_chamada_anonima_ficam_fora()
    {
        await using var fechada = Fabrica();
        using var cliente = fechada.CreateClient();
        using var saude = await cliente.GetAsync("/health");
        using var status = await cliente.GetAsync("/api/account/status");
        using var statusDeNovo = await cliente.GetAsync("/api/account/status");
        using var login = await cliente.PostAsJsonAsync("/api/account/login", new { email = "a@b.co", password = "senha-longa" });

        await using var aberta = new WebApplicationFactory<Program>();
        using var anonimo = aberta.CreateClient();
        using var organizacoes = await anonimo.GetAsync("/api/organizations");
        using var organizacoesDeNovo = await anonimo.GetAsync("/api/organizations");

        Assert.Equal(HttpStatusCode.OK, saude.StatusCode);
        Assert.False(saude.Headers.Contains(CabecalhosDeCache.Estado));
        Assert.False(status.Headers.Contains(CabecalhosDeCache.Estado));
        Assert.False(statusDeNovo.Headers.Contains(CabecalhosDeCache.Estado));
        Assert.False(login.Headers.Contains(CabecalhosDeCache.Estado));
        Assert.False(login.Headers.Contains(CabecalhosDeCache.Despejo));
        Assert.Equal(HttpStatusCode.OK, organizacoes.StatusCode);
        Assert.False(organizacoes.Headers.Contains(CabecalhosDeCache.Estado));
        Assert.False(organizacoesDeNovo.Headers.Contains(CabecalhosDeCache.Estado));
    }

    [Fact]
    public async Task Corpo_acima_de_256_kb_nao_e_guardado()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var id = Guid.NewGuid();
        var nome = new string('a', PoliticaDeCache.TamanhoMaximo);

        using var criado = await Enviar(cliente, OrganizacaoNome(id, nome));
        using var primeiro = await Pegar(cliente, "/api/organizations");
        var corpo = await primeiro.Content.ReadAsStringAsync();
        using var segundo = await Pegar(cliente, "/api/organizations");
        using var trilha = await Pegar(cliente, "/api/audit");
        using var trilhaDeNovo = await Pegar(cliente, "/api/audit");

        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        Assert.True(corpo.Length > PoliticaDeCache.TamanhoMaximo);
        Assert.False(primeiro.Headers.Contains(CabecalhosDeCache.Estado));
        Assert.False(primeiro.Headers.Contains(CabecalhosDeCache.Etiquetas));
        Assert.False(segundo.Headers.Contains(CabecalhosDeCache.Estado));
        Assert.Equal(CabecalhosDeCache.Falha, Estado(trilha));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(trilhaDeNovo));
    }

    [Fact]
    public async Task Escrita_de_concessao_despeja_engines()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var organizacao = Guid.NewGuid();
        using var gravada = await Enviar(cliente, Organizacao(organizacao, "concessao.example"));
        using var membros = await Pegar(cliente, "/api/members?organizationId=" + organizacao);
        using var membrosDeNovo = await Pegar(cliente, "/api/members?organizationId=" + organizacao);
        using var membro = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Put, "/api/members/" + Guid.NewGuid())
        {
            Content = JsonContent.Create(new { organizationId = organizacao, email = "ada@example.com", displayName = "Ada", role = "support" }),
        });
        using var membrosDepois = await Pegar(cliente, "/api/members?organizationId=" + organizacao);

        Assert.Equal(HttpStatusCode.OK, gravada.StatusCode);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(membros));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(membrosDeNovo));
        Assert.Equal(HttpStatusCode.OK, membro.StatusCode);
        var despejo = membro.Headers.GetValues(CabecalhosDeCache.Despejo).Single();
        Assert.Contains("members", despejo, StringComparison.Ordinal);
        Assert.Contains("audit", despejo, StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(membrosDepois));
    }

    [Fact]
    public async Task Escrita_de_assinatura_despeja_usage()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var organizacao = Guid.NewGuid();
        using var org = await Enviar(cliente, Organizacao(organizacao, "chave.example"));
        using var uso = await Pegar(cliente, "/api/audit");
        using var usoDeNovo = await Pegar(cliente, "/api/audit");
        using var chave = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Post, "/api/machine-keys")
        {
            Content = JsonContent.Create(new { organizationId = organizacao, name = "borda" }),
        });
        using var usoDepois = await Pegar(cliente, "/api/audit");

        Assert.Equal(HttpStatusCode.OK, org.StatusCode);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(uso));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(usoDeNovo));
        Assert.Equal(HttpStatusCode.OK, chave.StatusCode);
        var despejo = chave.Headers.GetValues(CabecalhosDeCache.Despejo).Single();
        Assert.Contains("machine-keys", despejo, StringComparison.Ordinal);
        Assert.Contains("audit", despejo, StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(usoDepois));
    }

    [Fact]
    public async Task Escrita_de_tenant_despeja_apps_e_governanca_despeja_as_tres_familias()
    {
        await using var fabrica = Fabrica();
        var organizacao = Guid.NewGuid();
        using var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await Enviar(cliente, Organizacao(organizacao, "planta.example"))).StatusCode);

        using var membros = await Pegar(cliente, "/api/members?organizationId=" + organizacao);
        using var membrosDeNovo = await Pegar(cliente, "/api/members?organizationId=" + organizacao);
        using var criado = await Enviar(cliente, new HttpRequestMessage(HttpMethod.Put, "/api/members/" + Guid.NewGuid())
        {
            Content = JsonContent.Create(new { organizationId = organizacao, email = "ada@example.com", displayName = "Ada", role = "support" }),
        });
        using var membrosDepois = await Pegar(cliente, "/api/members?organizationId=" + organizacao);

        Assert.Equal(CabecalhosDeCache.Falha, Estado(membros));
        Assert.Equal(CabecalhosDeCache.Acerto, Estado(membrosDeNovo));
        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        var despejoDoTenant = criado.Headers.GetValues(CabecalhosDeCache.Despejo).Single();
        Assert.Contains("members", despejoDoTenant, StringComparison.Ordinal);
        Assert.Contains("audit", despejoDoTenant, StringComparison.Ordinal);
        Assert.Equal(CabecalhosDeCache.Falha, Estado(membrosDepois));
    }

    private static WebApplicationFactory<Program> Fabrica(TimeProvider? relogio = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:ApiKey", Chave);
            if (relogio is not null)
            {
                builder.ConfigureTestServices(servicos => servicos.AddSingleton(relogio));
            }
        });
    }

    private static async Task<HttpResponseMessage> Pegar(HttpClient cliente, string caminho)
    {
        return await cliente.SendAsync(ComChave(caminho, Chave));
    }

    private static async Task<HttpResponseMessage> Enviar(HttpClient cliente, HttpRequestMessage pedido)
    {
        pedido.Headers.Add("X-Api-Key", Chave);
        return await cliente.SendAsync(pedido);
    }

    private static HttpRequestMessage ComChave(string caminho, string chave)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
        pedido.Headers.Add("X-Api-Key", chave);
        return pedido;
    }

    private static HttpRequestMessage Bearer(string caminho, string token)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
        pedido.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return pedido;
    }

    private static HttpRequestMessage OrganizacaoNome(Guid id, string nome)
    {
        var host = nome.Length > 40 ? id.ToString("N") + ".example" : nome.ToLowerInvariant() + ".example";
        return new HttpRequestMessage(HttpMethod.Put, "/api/organizations/" + id)
        {
            Content = JsonContent.Create(new { name = nome, host, idpIssuer = "https://idp.example" }),
        };
    }

    private static HttpRequestMessage Organizacao(Guid id, string host)
    {
        return new HttpRequestMessage(HttpMethod.Put, "/api/organizations/" + id)
        {
            Content = JsonContent.Create(new { name = "Local", host, idpIssuer = "https://idp.example" }),
        };
    }

    private static string Sessao(string subject, string organization)
    {
        return ValidadorDeJwt.EmitirSessao(
            "gatekeeper",
            "gatekeeper-console",
            "gatekeeper-local-session-key",
            DateTimeOffset.UtcNow.AddHours(1),
            subject,
            organization,
            new[] { "Staff.Admin" }).Valor;
    }

    private static string Estado(HttpResponseMessage resposta)
    {
        return resposta.Headers.GetValues(CabecalhosDeCache.Estado).Single();
    }

    private sealed class Relogio : TimeProvider
    {
        public Relogio(DateTimeOffset agora)
        {
            Agora = agora;
        }

        public DateTimeOffset Agora { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            return Agora;
        }
    }
}
