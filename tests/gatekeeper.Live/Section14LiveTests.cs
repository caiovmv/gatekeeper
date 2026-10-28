using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace gatekeeper.Live;

[Collection("live")]
public sealed class Section14LiveTests
{
    private readonly StaffSession _staff;

    public Section14LiveTests(StaffSession staff)
    {
        _staff = staff;
    }

    [LiveFact]
    public async Task Isolation_hides_an_unassigned_organization_and_an_auditor_cannot_write()
    {
        var left = await CreateOrganizationAsync("Atribuida");
        var right = await CreateOrganizationAsync("Alheia");
        var supportEmail = "support-" + Guid.NewGuid().ToString("N")[..8] + "@gatekeeper.test";
        var auditorEmail = "auditor-" + Guid.NewGuid().ToString("N")[..8] + "@gatekeeper.test";
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PutAsJsonAsync("api/members/" + Guid.NewGuid(), new { organizationId = left, email = supportEmail, displayName = "Suporte", role = "support", password = LiveAccounts.SupportPassword })).StatusCode);
        var auditor = await _staff.Client.PutAsJsonAsync("api/members/" + Guid.NewGuid(), new { organizationId = left, email = auditorEmail, displayName = "Auditor", role = "Staff.Auditor", password = LiveAccounts.SupportPassword });
        Assert.Equal(HttpStatusCode.OK, auditor.StatusCode);

        using var support = Cluster.Open();
        Cluster.Authorize(support, await Cluster.Login(support, supportEmail, LiveAccounts.SupportPassword));
        var visible = await support.GetFromJsonAsync<JsonElement>("api/organizations");
        Assert.Contains(visible.EnumerateArray(), item => item.GetProperty("id").GetGuid() == left);
        Assert.DoesNotContain(visible.EnumerateArray(), item => item.GetProperty("id").GetGuid() == right);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("api/members?organizationId=" + right)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("api/usage/" + right)).StatusCode);

        using var reader = Cluster.Open();
        Cluster.Authorize(reader, await Cluster.Login(reader, auditorEmail, LiveAccounts.SupportPassword));
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync("api/organizations/" + Guid.NewGuid(), new { name = "Negado", host = "negado-auditor.test", idpIssuer = "", directoryId = "" })).StatusCode);
        var audit = await reader.GetFromJsonAsync<JsonElement>("api/audit");
        Assert.Contains(audit.EnumerateArray(), item => item.GetProperty("action").GetString() == "audit.read");
    }

    [LiveFact]
    public async Task Billing_webhook_and_redaction_hold_on_the_cluster()
    {
        var organization = await CreateOrganizationAsync("Tarifa viva");
        var planId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PutAsJsonAsync("api/plans/" + planId, new { name = "Tarifa viva", price = 10, seatLimit = 5 })).StatusCode);
        var subscriptionId = Guid.NewGuid();
        var saved = await _staff.Client.PutAsJsonAsync("api/subscriptions/" + subscriptionId, new { organizationId = organization, planId, startsOn = "2026-01-01T00:00:00Z", endsOn = "2027-01-01T00:00:00Z" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(10m, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("agreedPrice").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PutAsJsonAsync("api/plans/" + planId, new { name = "Tarifa viva", price = 99, seatLimit = 5 })).StatusCode);
        var rows = await _staff.Client.GetFromJsonAsync<JsonElement>("api/subscriptions");
        Assert.Contains(rows.EnumerateArray(), item => item.GetProperty("id").GetGuid() == subscriptionId && item.GetProperty("agreedPrice").GetDecimal() == 10m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _staff.Client.PutAsJsonAsync("api/subscriptions/" + Guid.NewGuid(), new { organizationId = organization, planId = Guid.NewGuid(), startsOn = "2026-01-01T00:00:00Z", endsOn = "2027-01-01T00:00:00Z" })).StatusCode);

        var correlation = "password=super-secret-value";
        var call = new { organizationId = organization, correlation, engine = "Jev", durationMs = 10, cause = "completed", recordingConsented = false };
        using var peer = Cluster.Open();
        Cluster.Authorize(peer, _staff.Token);
        var completed = await Task.WhenAll(_staff.Client.PostAsJsonAsync("api/calls", call), peer.PostAsJsonAsync("api/calls", call));
        Assert.Equal(HttpStatusCode.OK, completed[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed[1].StatusCode);
        Assert.Equal(1, (await _staff.Client.GetFromJsonAsync<JsonElement>("api/usage/" + organization)).GetProperty("calls").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _staff.Client.PostAsJsonAsync("api/calls", new { organizationId = organization, correlation = "outra-" + Guid.NewGuid().ToString("N")[..8], engine = "motor-inexistente", durationMs = 1, cause = "completed", recordingConsented = false })).StatusCode);
        var audit = await _staff.Client.GetFromJsonAsync<JsonElement>("api/audit");
        Assert.Contains(audit.EnumerateArray(), item => item.GetProperty("resource").GetString() == "password=***");

        var tenant = Guid.NewGuid();
        var registered = await _staff.Client.PostAsJsonAsync("api/integrations", new { tenantId = tenant, name = "caixa" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var created = await registered.Content.ReadFromJsonAsync<JsonElement>();
        var secret = created.GetProperty("secret").GetString() ?? string.Empty;
        var id = created.GetProperty("id").GetGuid();
        var payload = "evento-vivo";
        using var mismatched = Request(id, new { tenantId = Guid.NewGuid(), sourceEventId = "evt-vivo-1", payload }, Sign(secret, payload));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _staff.Client.SendAsync(mismatched)).StatusCode);
        using var accepted = Request(id, new { tenantId = tenant, sourceEventId = "evt-vivo-2", payload }, Sign(secret, payload));
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.SendAsync(accepted)).StatusCode);
        using var replay = Request(id, new { tenantId = tenant, sourceEventId = "evt-vivo-2", payload }, Sign(secret, payload));
        Assert.Equal(HttpStatusCode.Conflict, (await _staff.Client.SendAsync(replay)).StatusCode);

        using var anonymous = Cluster.Open();
        var ready = await anonymous.GetStringAsync("health/ready");
        Assert.Contains("sql", ready, StringComparison.Ordinal);
        var status = await _staff.Client.GetFromJsonAsync<JsonElement>("api/governance/status");
        Assert.False(status.GetProperty("wormApproved").GetBoolean());
        Assert.False(status.GetProperty("sloMeasured").GetBoolean());
        Assert.False(status.GetProperty("restoreDrillPassed").GetBoolean());
        Assert.Equal("memory", status.GetProperty("audit").GetString());
        Assert.Equal("sql", status.GetProperty("billing").GetString());
        var health = await anonymous.GetAsync("api/account/status");
        Assert.Equal("no-store", health.Headers.GetValues("Cache-Control").Single());
    }

    private async Task<Guid> CreateOrganizationAsync(string name)
    {
        var id = Guid.NewGuid();
        var response = await _staff.Client.PutAsJsonAsync("api/organizations/" + id, new { name, host = name.ToLowerInvariant().Replace(' ', '-') + "-" + id.ToString("N")[..6] + ".test", idpIssuer = "", directoryId = "" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private static HttpRequestMessage Request(Guid id, object body, string signature)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "api/integrations/" + id + "/events")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.TryAddWithoutValidation("X-Signature", signature);
        return message;
    }

    private static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
