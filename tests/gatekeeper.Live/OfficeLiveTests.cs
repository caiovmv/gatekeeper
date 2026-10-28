using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace gatekeeper.Live;

[Collection("live")]
public sealed class OfficeLiveTests
{
    private readonly StaffSession _staff;

    public OfficeLiveTests(StaffSession staff)
    {
        _staff = staff;
    }

    [LiveFact]
    public async Task Anonymous_call_is_denied_and_staff_can_sell_a_plan()
    {
        using var anonymous = Cluster.Open();
        var denied = await anonymous.PutAsJsonAsync("api/plans/" + Guid.NewGuid(), new { name = "Nope", price = 1, seatLimit = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var organizationId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PutAsJsonAsync("api/organizations/" + organizationId, new { name = "Live " + organizationId.ToString("N")[..8], host = organizationId.ToString("N") + ".test", idpIssuer = "", directoryId = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PutAsJsonAsync("api/plans/" + planId, new { name = "Live", price = 10, seatLimit = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PutAsJsonAsync("api/plans/" + planId, new { name = "Live Plus", price = 12, seatLimit = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.PutAsJsonAsync("api/subscriptions/" + Guid.NewGuid(), new
        {
            organizationId,
            planId,
            startsOn = "2026-01-01T00:00:00Z",
            endsOn = "2027-01-01T00:00:00Z",
        })).StatusCode);

        var supportEmail = "support-" + Guid.NewGuid().ToString("N")[..8] + "@gatekeeper.test";
        var member = await _staff.Client.PutAsJsonAsync("api/members/" + Guid.NewGuid(), new
        {
            organizationId,
            email = supportEmail,
            displayName = "Support",
            role = "support",
            password = LiveAccounts.SupportPassword,
        });
        var memberBody = await member.Content.ReadAsStringAsync();
        Assert.True(member.IsSuccessStatusCode, memberBody);

        var recorded = await _staff.Client.PostAsJsonAsync("api/calls", new
        {
            organizationId,
            correlation = "live-" + organizationId.ToString("N")[..8],
            engine = "Jev",
            durationMs = 1200,
            cause = "completed",
            recordingConsented = true,
        });
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var usage = await _staff.Client.GetFromJsonAsync<JsonElement>("api/usage/" + organizationId);
        Assert.Equal(1, usage.GetProperty("calls").GetInt32());
        Assert.Equal("warning", usage.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await _staff.Client.DeleteAsync("api/plans/" + planId)).StatusCode);
        var plans = await _staff.Client.GetFromJsonAsync<JsonElement>("api/plans");
        Assert.DoesNotContain(plans.EnumerateArray(), item => item.GetProperty("id").GetGuid() == planId);

        using var support = Cluster.Open();
        Cluster.Authorize(support, await Cluster.Login(support, supportEmail, LiveAccounts.SupportPassword));
        var blocked = await support.PutAsJsonAsync("api/plans/" + Guid.NewGuid(), new { name = "Nope", price = 1, seatLimit = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        var audit = await _staff.Client.GetFromJsonAsync<JsonElement>("api/audit");
        Assert.Contains(audit.EnumerateArray(), item => item.GetProperty("action").GetString() == "member.create");

        var governance = await _staff.Client.GetFromJsonAsync<JsonElement>("api/governance/status");
        Assert.False(governance.GetProperty("wormApproved").GetBoolean());
        Assert.False(governance.GetProperty("sloMeasured").GetBoolean());
        Assert.False(governance.GetProperty("restoreDrillPassed").GetBoolean());
    }
}
