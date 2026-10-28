using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using gatekeeper.Authorization;
using gatekeeper.Billing;
using gatekeeper.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gatekeeper.Tests.Billing;

public class BillingTests
{
    [Fact]
    public async Task Light_profile_saves_organization()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var organizationId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/organizations/" + organizationId, new
        {
            name = "Local",
            host = "localhost",
            idpIssuer = "https://idp.example",
        })).StatusCode);

        var organizations = await client.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.Equal("localhost", organizations[0].GetProperty("host").GetString());
        Assert.Equal("https://idp.example", factory.Services.GetRequiredService<IOrganizationDirectory>().IssuerForHost("localhost"));
    }

    [Fact]
    public async Task Denied_write_does_not_reach_the_store()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAuthorizer, DenyAuthorizer>();
            }));
        using var client = factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/organizations/" + Guid.NewGuid(), new
        {
            name = "Local",
            host = "negado.example",
            idpIssuer = "",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Services.GetRequiredService<IBillingStore>().Organizations());
    }

    private sealed class DenyAuthorizer : IAuthorizer
    {
        public Task<bool> AllowAsync(DecisionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
    }
}
