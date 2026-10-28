using System.Net.Http.Json;
using System.Text.Json;
using gatekeeper.Authorization;

namespace gatekeeper.Authorization.Adapters;

public sealed class CerbosAuthorizer : IAuthorizer
{
    private readonly HttpClient _client;

    public CerbosAuthorizer(HttpClient client)
    {
        _client = client;
    }

    public async Task<bool> AllowAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            requestId = Guid.NewGuid().ToString("N"),
            principal = new
            {
                id = request.Principal.Subject,
                roles = request.Principal.Roles,
                attr = new { organization = request.Principal.Organization },
            },
            resources = new[]
            {
                new
                {
                    actions = new[] { request.Action },
                    resource = new
                    {
                        kind = request.Resource.Kind,
                        id = request.Resource.Id,
                        attr = new { organization = request.Resource.Organization },
                    },
                },
            },
        };

        using var response = await _client.PostAsJsonAsync("/api/check/resources", body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return CerbosDecision.Allows(document, request.Action);
    }
}

public static class CerbosDecision
{
    public static bool Allows(JsonElement document, string action)
    {
        if (!document.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (actions.TryGetProperty(action, out var effect) && string.Equals(effect.GetString(), "EFFECT_ALLOW", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
