using System.Text.Json;
using gatekeeper.Authorization;
using gatekeeper.Authorization.Adapters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gatekeeper.Tests.Authorization;

public class AuthorizerTests
{
    [Fact]
    public async Task Empty_endpoint_allows_the_light_profile()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var authorizer = factory.Services.GetRequiredService<IAuthorizer>();
        var allowed = await authorizer.AllowAsync(
            new DecisionRequest(
                new Principal("system", "local", new[] { "admin" }),
                "create",
                new Resource("plan", "basic", "local")),
            CancellationToken.None);

        Assert.IsType<TimedAuthorizer>(authorizer);
        Assert.True(allowed);
    }

    [Fact]
    public void Cerbos_allow_effect_is_read_from_the_check_response()
    {
        using var document = JsonDocument.Parse("{\"results\":[{\"actions\":{\"create\":\"EFFECT_ALLOW\",\"delete\":\"EFFECT_DENY\"}}]}");

        Assert.True(CerbosDecision.Allows(document.RootElement, "create"));
        Assert.False(CerbosDecision.Allows(document.RootElement, "delete"));
    }
}
