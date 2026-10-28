using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace gatekeeper.Live;

[Collection("live")]
public sealed class PlanAcceptanceLiveTests
{
    private readonly StaffSession _staff;

    public PlanAcceptanceLiveTests(StaffSession staff)
    {
        _staff = staff;
    }

    [LiveFact]
    public async Task Isolation_keeps_a_member_inside_one_organization()
    {
        using var anonymous = Cluster.Open();
        var denied = await anonymous.GetAsync("api/organizations");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var left = await CreateOrganizationAsync("Iso esquerda");
        var right = await CreateOrganizationAsync("Iso direita");
        var email = "iso-" + Guid.NewGuid().ToString("N")[..8] + "@gatekeeper.test";
        var member = await _staff.Client.PutAsJsonAsync("api/members/" + Guid.NewGuid(), new
        {
            organizationId = left,
            email,
            displayName = "Iso",
            role = "support",
            password = LiveAccounts.SupportPassword,
        });
        Assert.Equal(HttpStatusCode.OK, member.StatusCode);

        var foreign = await _staff.Client.GetFromJsonAsync<JsonElement>("api/members?organizationId=" + right);
        foreach (var row in foreign.EnumerateArray())
        {
            Assert.NotEqual(email, row.GetProperty("email").GetString());
        }
    }

    [LiveFact]
    public async Task Identity_rejects_a_bad_password_and_closed_federation()
    {
        using var anonymous = Cluster.Open();
        var password = await anonymous.PostAsJsonAsync("api/account/login", new { email = LiveAccounts.StaffEmail, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, password.StatusCode);

        var oidc = await anonymous.PostAsJsonAsync("api/account/oidc/callback", new { code = "code", state = "state", expectedState = "other", nonce = "nonce", expectedNonce = "nonce" });
        Assert.Equal(HttpStatusCode.Unauthorized, oidc.StatusCode);

        var saml = await anonymous.PostAsJsonAsync("api/account/saml/acs", new { response = "response", expectedRequestId = "request" });
        Assert.Equal(HttpStatusCode.Forbidden, saml.StatusCode);
        Assert.Contains("saml is closed", await saml.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var webhook = await anonymous.PostAsJsonAsync("api/webhooks/calls", new { integrationId = Guid.NewGuid(), tenantId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, webhook.StatusCode);

        using var session = Cluster.Open();
        Cluster.Authorize(session, await Cluster.Login(session, LiveAccounts.StaffEmail, LiveAccounts.StaffPassword));
        var logout = await session.PostAsync("api/account/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains("signed-out", await logout.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("api/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.GetAsync("api/account/me")).StatusCode);
    }

    [LiveFact]
    public async Task Credential_is_returned_once_and_an_unknown_key_stays_closed()
    {
        var organization = await CreateOrganizationAsync("Chave");
        var issued = await _staff.Client.PostAsJsonAsync("api/machine-keys", new { organizationId = organization, name = "balcao" });
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var created = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var secret = created.GetProperty("secret").GetString();
        var id = created.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrWhiteSpace(secret));

        var listed = await _staff.Client.GetFromJsonAsync<JsonElement>("api/machine-keys?organizationId=" + organization);
        var stored = false;
        foreach (var row in listed.EnumerateArray())
        {
            if (row.GetProperty("id").GetGuid() != id)
            {
                continue;
            }

            stored = true;
            Assert.False(row.TryGetProperty("secret", out _));
        }

        Assert.True(stored);
        var revoked = await _staff.Client.DeleteAsync("api/machine-keys/" + id);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var rotate = await _staff.Client.PostAsync("api/governance/keys/chave-inexistente/rotate", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rotate.StatusCode);
        var extend = await _staff.Client.PostAsJsonAsync("api/governance/keys/chave-inexistente/extend", new { days = 1, approvalId = Guid.Empty, approver = Guid.Empty });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, extend.StatusCode);
    }

    [LiveFact]
    public async Task Mutation_conflicts_when_the_same_key_changes_and_a_missing_tenant_stays_refused()
    {
        var key = Guid.NewGuid().ToString("N");
        var organization = Guid.NewGuid();
        using var first = Request("api/tenants", key, new { organizationId = organization, name = "Primeiro" });
        using var changed = Request("api/tenants", key, new { organizationId = organization, name = "Segundo" });
        var created = await _staff.Client.SendAsync(first);
        var conflict = await _staff.Client.SendAsync(changed);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, created.StatusCode);
        Assert.Contains("organization missing", await created.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var actor = Guid.NewGuid();
        var approval = Guid.NewGuid();
        var requested = await _staff.Client.PostAsJsonAsync("api/governance/approvals", new { id = approval, actor });
        var approved = await _staff.Client.PostAsJsonAsync("api/governance/approvals/" + approval + "/approve", new { id = approval, actor });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, approved.StatusCode);
        Assert.Contains("approval refused", await approved.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var glass = await _staff.Client.PostAsJsonAsync("api/governance/break-glass", new { id = Guid.NewGuid(), first = actor, second = actor });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, glass.StatusCode);
        var suspended = await _staff.Client.PostAsync("api/governance/tenants/" + Guid.NewGuid() + "/suspend", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, suspended.StatusCode);
    }

    [LiveFact]
    public async Task Provenance_records_one_call_when_the_same_correlation_arrives_twice()
    {
        var organization = await CreateOrganizationAsync("Chamada");
        var correlation = "corr-" + Guid.NewGuid().ToString("N")[..12];
        var body = new
        {
            organizationId = organization,
            correlation,
            engine = "Jev",
            durationMs = 1200,
            cause = "normal",
            recordingConsented = true,
        };
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PostAsJsonAsync("api/calls", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PostAsJsonAsync("api/calls", body)).StatusCode);
        var usage = await _staff.Client.GetFromJsonAsync<JsonElement>("api/usage/" + organization);
        Assert.Equal(1, usage.GetProperty("calls").GetInt32());

        var audit = await _staff.Client.GetFromJsonAsync<JsonElement>("api/audit");
        var found = false;
        foreach (var row in audit.EnumerateArray())
        {
            if (row.GetProperty("resource").GetString() == correlation)
            {
                found = true;
            }
        }

        Assert.True(found);
        var unknown = await _staff.Client.PostAsJsonAsync("api/calls", new
        {
            organizationId = organization,
            correlation = "outra-" + Guid.NewGuid().ToString("N")[..8],
            engine = "motor-inexistente",
            durationMs = 1,
            cause = "normal",
            recordingConsented = false,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        var missing = await _staff.Client.PostAsJsonAsync("api/governance/calls/" + Guid.NewGuid() + "/attribute", new { status = "attributed" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
    }

    [LiveFact]
    public async Task Billing_keeps_the_subscription_when_the_plan_price_changes()
    {
        var organization = await CreateOrganizationAsync("Tarifa");
        var planId = Guid.NewGuid();
        var plan = await _staff.Client.PutAsJsonAsync("api/plans/" + planId, new { name = "Tarifa", price = 10m, seatLimit = 5 });
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        var subscriptionId = Guid.NewGuid();
        var subscription = await _staff.Client.PutAsJsonAsync("api/subscriptions/" + subscriptionId, new
        {
            organizationId = organization,
            planId,
            startsOn = DateTimeOffset.UtcNow,
            endsOn = DateTimeOffset.UtcNow.AddDays(30),
        });
        Assert.Equal(HttpStatusCode.OK, subscription.StatusCode);
        var changed = await _staff.Client.PutAsJsonAsync("api/plans/" + planId, new { name = "Tarifa", price = 99m, seatLimit = 5 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var rows = await _staff.Client.GetFromJsonAsync<JsonElement>("api/subscriptions");
        var kept = false;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("id").GetGuid() == subscriptionId)
            {
                kept = true;
                Assert.Equal(planId, row.GetProperty("planId").GetGuid());
            }
        }

        Assert.True(kept);
    }

    [LiveFact]
    public async Task Support_cannot_write_the_catalog()
    {
        var organization = await CreateOrganizationAsync("Suporte");
        var email = "support-" + Guid.NewGuid().ToString("N")[..8] + "@gatekeeper.test";
        var member = await _staff.Client.PutAsJsonAsync("api/members/" + Guid.NewGuid(), new
        {
            organizationId = organization,
            email,
            displayName = "Suporte",
            role = "support",
            password = LiveAccounts.SupportPassword,
        });
        Assert.Equal(HttpStatusCode.OK, member.StatusCode);
        using var client = Cluster.Open();
        Cluster.Authorize(client, await Cluster.Login(client, email, LiveAccounts.SupportPassword));
        var write = await client.PutAsJsonAsync("api/organizations/" + Guid.NewGuid(), new { name = "Negado", host = "negado.test", idpIssuer = "", directoryId = "" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [LiveFact]
    public async Task Delivery_sends_the_closed_headers_and_the_proof_flags_stay_false()
    {
        using var anonymous = Cluster.Open();
        var health = await anonymous.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", health.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", health.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

        var ready = await anonymous.GetStringAsync("health/ready");
        Assert.Contains("sql", ready, StringComparison.Ordinal);
        var status = await _staff.Client.GetFromJsonAsync<JsonElement>("api/governance/status");
        Assert.False(status.GetProperty("wormApproved").GetBoolean());
        Assert.False(status.GetProperty("sloMeasured").GetBoolean());
        Assert.False(status.GetProperty("restoreDrillPassed").GetBoolean());
        Assert.False(status.GetProperty("lockdown").GetBoolean());
        Assert.Equal("memory", status.GetProperty("audit").GetString());
        Assert.Equal("sql", status.GetProperty("billing").GetString());
        var held = await _staff.Client.PostAsJsonAsync("api/governance/holds", new { category = "audit", reason = "aceite" });
        Assert.Equal(HttpStatusCode.OK, held.StatusCode);
    }

    private async Task<Guid> CreateOrganizationAsync(string name)
    {
        var id = Guid.NewGuid();
        var host = name.ToLowerInvariant().Replace(' ', '-') + "-" + id.ToString("N")[..6] + ".test";
        var response = await _staff.Client.PutAsJsonAsync("api/organizations/" + id, new { name, host, idpIssuer = "", directoryId = "" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private static HttpRequestMessage Request(string path, string key, object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return message;
    }
}
