using System.Net.Http.Json;
using gatekeeper.Authorization;

namespace gatekeeper.Authorization.Adapters;

public sealed class CerbosPolicyPublisher : IPolicyPublisher
{
    private readonly HttpClient _client;

    public CerbosPolicyPublisher(HttpClient client)
    {
        _client = client;
    }

    public async Task PublishAsync(IReadOnlyList<AccessRule> rules, CancellationToken cancellationToken)
    {
        var batch = PolicyExportBuilder.Build(rules);
        var policies = new List<object>();
        foreach (var policy in batch.Active)
        {
            var exported = new List<object>();
            foreach (var rule in policy.Rules)
            {
                exported.Add(new
                {
                    actions = new[] { rule.Action },
                    effect = rule.Effect == "deny" ? "EFFECT_DENY" : "EFFECT_ALLOW",
                    roles = new[] { rule.Role },
                });
            }

            policies.Add(new
            {
                apiVersion = "api.cerbos.dev/v1",
                resourcePolicy = new
                {
                    version = "default",
                    resource = policy.Resource,
                    rules = exported,
                },
            });
        }

        if (policies.Count > 0)
        {
            await SendAsync("/admin/policy", new { policies }, cancellationToken);
        }

        if (batch.DisabledResources.Count > 0)
        {
            var ids = new List<string>();
            foreach (var resource in batch.DisabledResources)
            {
                ids.Add("resource." + resource + ".vdefault");
            }

            await SendAsync("/admin/policy/disable", new { ids }, cancellationToken);
        }
    }

    private async Task SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(path, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException("Cerbos rejected the policy set (" + (int)response.StatusCode + "). " + detail);
        }
    }
}
