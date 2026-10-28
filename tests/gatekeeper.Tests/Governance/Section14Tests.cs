using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gatekeeper.Governance;
using gatekeeper.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace gatekeeper.Tests.Governance;

public class Section14Tests
{
    [Fact]
    public async Task Isolation_assignment_and_closed_federation_hold()
    {
        await using var factory = Factory();
        using var admin = factory.CreateClient();
        Authorize(admin, await Token(admin, "/api/account/setup", new { email = "owner@localhost", password = "owner-pass", displayName = "Owner" }));
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/organizations/" + left, new { name = "Esquerda", host = "esquerda.test", idpIssuer = "", directoryId = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/organizations/" + right, new { name = "Direita", host = "direita.test", idpIssuer = "", directoryId = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/members/" + Guid.NewGuid(), new { organizationId = left, email = "support@localhost", displayName = "Suporte", role = "support", password = "support-pass" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/members/" + Guid.NewGuid(), new { organizationId = left, email = "auditor@localhost", displayName = "Auditor", role = "Staff.Auditor", password = "auditor-pass" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync("/api/members/" + Guid.NewGuid(), new { organizationId = Guid.NewGuid(), email = "orfao@localhost", displayName = "Orfao", role = "support", password = "orfao-pass-1" })).StatusCode);

        using var support = factory.CreateClient();
        Authorize(support, await Token(support, "/api/account/login", new { email = "support@localhost", password = "support-pass" }));
        var visible = await support.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.Contains(visible.EnumerateArray(), item => item.GetProperty("id").GetGuid() == left);
        Assert.DoesNotContain(visible.EnumerateArray(), item => item.GetProperty("id").GetGuid() == right);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("/api/members?organizationId=" + right)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PutAsJsonAsync("/api/organizations/" + Guid.NewGuid(), new { name = "Negado", host = "negado.test", idpIssuer = "", directoryId = "" })).StatusCode);

        using var auditor = factory.CreateClient();
        Authorize(auditor, await Token(auditor, "/api/account/login", new { email = "auditor@localhost", password = "auditor-pass" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PutAsJsonAsync("/api/organizations/" + Guid.NewGuid(), new { name = "Negado", host = "auditor.test", idpIssuer = "", directoryId = "" })).StatusCode);
        var audit = await auditor.GetFromJsonAsync<JsonElement>("/api/audit");
        Assert.Contains(audit.EnumerateArray(), item => item.GetProperty("action").GetString() == "audit.read");

        var expired = ValidadorDeJwt.EmitirSessao("gatekeeper", "gatekeeper-console", "gatekeeper-local-session-key", DateTimeOffset.UtcNow.AddMinutes(-1), "subject", "org", new[] { "Staff.Admin" }, "owner@localhost");
        using var stale = factory.CreateClient();
        stale.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired.Valor);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/api/account/me")).StatusCode);

        var first = await Token(admin, "/api/account/login", new { email = "owner@localhost", password = "owner-pass" });
        using var retired = factory.CreateClient();
        Authorize(retired, first);
        Assert.Equal(HttpStatusCode.OK, (await retired.PostAsync("/api/account/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await retired.GetAsync("/api/account/me")).StatusCode);
        Authorize(admin, await Token(admin, "/api/account/login", new { email = "owner@localhost", password = "owner-pass" }));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/account/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/account/oidc/callback", new { code = "code", state = "state", expectedState = "state", nonce = "nonce", expectedNonce = "nonce" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().PostAsJsonAsync("/api/account/saml/acs", new { response = "<Response/>", expectedRequestId = "request" })).StatusCode);
        Assert.False(OidcChecks.Validate(new OidcLogin("code", "state", "state", "nonce", "nonce", "issuer", "issuer", "aud", "aud", "plain", DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, false, "user")).Ok);
        Assert.False(OidcChecks.Validate(new OidcLogin("code", "state", "state", "nonce", "nonce", "issuer", "issuer", "aud", "aud", "S256", DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, true, "user")).Ok);
        Assert.Equal("wrapping", SamlChecks.Validate("<r><Assertion/><Assertion/></r>", "aud", "id", "dest", "dest", "cert").Detail);
    }

    [Fact]
    public async Task Billing_provenance_and_webhook_stay_closed()
    {
        await using var factory = Factory();
        using var admin = factory.CreateClient();
        Authorize(admin, await Token(admin, "/api/account/setup", new { email = "billing@localhost", password = "billing-pass", displayName = "Billing" }));
        var organization = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/organizations/" + organization, new { name = "Tarifa", host = "tarifa.test", idpIssuer = "", directoryId = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync("/api/organizations/" + Guid.NewGuid(), new { name = "", host = "", idpIssuer = "" })).StatusCode);

        var correlation = "password=super-secret-value";
        var ingested = await admin.PostAsJsonAsync("/api/ingest/audit", new { actor = "billing", action = "ingest", resource = correlation, outcome = "allow", correlation = "corr-1" });
        Assert.Equal(HttpStatusCode.OK, ingested.StatusCode);
        var audit = await admin.GetFromJsonAsync<JsonElement>("/api/audit");
        Assert.Contains(audit.EnumerateArray(), item => item.GetProperty("resource").GetString() == "password=***");
        Assert.DoesNotContain(audit.EnumerateArray(), item => (item.GetProperty("resource").GetString() ?? string.Empty).Contains("super-secret-value", StringComparison.Ordinal));

        var ledger = new GovernanceLedger();
        var call = Guid.NewGuid();
        Assert.True(ledger.IngestCall(call, null, null, "webhook").Ok);
        Assert.False(ledger.Billable(call));
        var approval = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.True(ledger.RequestApproval(approval, actor, now).Ok);
        Assert.False(ledger.Approve(approval, Guid.NewGuid(), now.AddMinutes(31)).Ok);
        Assert.False(ObjectLockRules.CanDeleteVersion(true, now.AddDays(1), now, true));
        Assert.False(ObjectLockRules.CanShortenRetention(true, now.AddYears(1), now));
        Assert.False(GovernanceProof.WormApproved);
        Assert.False(GovernanceProof.SloMeasured);
        Assert.False(GovernanceProof.RestoreDrillPassed);
        Assert.Equal(GovernanceMode.Abort, GovernanceEnvironment.Resolve("leve", "producao", true));
        Assert.Equal(GovernanceMode.Abort, GovernanceEnvironment.Resolve("teste", "homologacao", false));
    }

    [Fact]
    public async Task Revoked_machine_key_does_not_open_a_configured_edge()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("App:Perfil", "teste");
                builder.UseSetting("Auth:ApiKey", "edge-secret");
            });
        using var admin = factory.CreateClient();
        Authorize(admin, await Token(admin, "/api/account/setup", new { email = "edge@localhost", password = "edge-pass-1", displayName = "Edge" }));
        var organization = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/organizations/" + organization, new { name = "Borda", host = "borda.test", idpIssuer = "", directoryId = "" })).StatusCode);
        var issued = await admin.PostAsJsonAsync("/api/machine-keys", new { organizationId = organization, name = "caixa" });
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var created = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var secret = created.GetProperty("secret").GetString() ?? string.Empty;
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/machine-keys/" + created.GetProperty("id").GetGuid())).StatusCode);

        using var edge = factory.CreateClient();
        edge.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await edge.GetAsync("/api/organizations")).StatusCode);
    }

    private static WebApplicationFactory<Program> Factory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("App:Perfil", "teste"));
    }

    private static async Task<string> Token(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("token").GetString() ?? string.Empty;
    }

    private static void Authorize(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
