using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using gatekeeper.Billing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gatekeeper.Tests.Billing;

public class OfficeTests
{
    [Fact]
    public async Task Backoffice_covers_members_keys_usage_engines_audit_calls_and_health()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var organizationId = Guid.NewGuid();
        await client.PutAsJsonAsync("/api/organizations/" + organizationId, new { name = "Local", host = "localhost", idpIssuer = "" });

        var memberId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/members/" + memberId, new
        {
            organizationId,
            email = "ada@example.com",
            displayName = "Ada",
            role = "admin",
        })).StatusCode);
        var members = await client.GetFromJsonAsync<JsonElement>("/api/members?organizationId=" + organizationId);
        Assert.Equal("ada@example.com", members[0].GetProperty("email").GetString());

        var issued = await (await client.PostAsJsonAsync("/api/machine-keys", new { organizationId, name = "voice-edge" })).Content.ReadFromJsonAsync<JsonElement>();
        var secret = issued.GetProperty("secret").GetString();
        Assert.False(string.IsNullOrWhiteSpace(secret));
        var keys = await client.GetFromJsonAsync<JsonElement>("/api/machine-keys?organizationId=" + organizationId);
        Assert.Equal(issued.GetProperty("prefix").GetString(), keys[0].GetProperty("prefix").GetString());
        Assert.False(keys[0].TryGetProperty("secret", out _));
        Assert.True(factory.Services.GetRequiredService<IMachineKeyGate>().Touch(secret!, DateTimeOffset.UtcNow));
        var used = await client.GetFromJsonAsync<JsonElement>("/api/machine-keys?organizationId=" + organizationId);
        Assert.NotEqual(JsonValueKind.Null, used[0].GetProperty("lastUsedOn").ValueKind);

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit");
        Assert.Contains(audit.EnumerateArray(), item => item.GetProperty("action").GetString() == "member.create");

        var health = await client.GetFromJsonAsync<JsonElement>("/api/platform/health");
        Assert.Contains(health.EnumerateArray(), item => item.GetProperty("name").GetString() == "zitadel" && item.GetProperty("status").GetString() == "not-configured");
        Assert.Contains(health.EnumerateArray(), item => item.GetProperty("name").GetString() == "cerbos" && item.GetProperty("status").GetString() == "not-configured");
    }
}

