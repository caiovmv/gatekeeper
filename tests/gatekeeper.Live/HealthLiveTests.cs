using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace gatekeeper.Live;

public sealed class HealthLiveTests
{
    [LiveFact]
    public async Task Process_probe_stays_on_the_process()
    {
        using var client = Cluster.Open();
        var response = await client.GetAsync("health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("processo", body, StringComparison.Ordinal);
        Assert.DoesNotContain("zitadel", body, StringComparison.Ordinal);
        Assert.DoesNotContain("cerbos", body, StringComparison.Ordinal);
    }

    [LiveFact]
    public async Task Readiness_reports_the_configured_identity_and_authorizer()
    {
        using var client = Cluster.Open();
        var response = await client.GetAsync("health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("federated", body, StringComparison.Ordinal);
        Assert.Contains("cerbos", body, StringComparison.Ordinal);
        Assert.Contains("sql", body, StringComparison.Ordinal);
    }

    [LiveFact]
    public async Task Platform_health_reaches_zitadel_and_cerbos()
    {
        using var client = Cluster.Open();
        var response = await client.GetAsync("api/platform/health");
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(report.EnumerateArray(), item => item.GetProperty("name").GetString() == "zitadel" && item.GetProperty("status").GetString() == "healthy");
        Assert.Contains(report.EnumerateArray(), item => item.GetProperty("name").GetString() == "cerbos" && item.GetProperty("status").GetString() == "healthy");
    }

    [LiveFact]
    public async Task Account_directory_is_zitadel_and_local_setup_is_closed()
    {
        using var client = Cluster.Open();
        var status = await client.GetFromJsonAsync<JsonElement>("api/account/status");

        Assert.False(status.GetProperty("setupRequired").GetBoolean());
        Assert.Equal("zitadel", status.GetProperty("provider").GetString());
    }
}
