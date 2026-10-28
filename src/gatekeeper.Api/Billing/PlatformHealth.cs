using gatekeeper.Health;

namespace gatekeeper.Api;

public sealed record ComponentHealth(string Name, string Status, string Detail);

public static class PlatformHealth
{
    public static async Task<IReadOnlyList<ComponentHealth>> ReadAsync(IEnumerable<ISondaDeInfraestrutura> probes, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var report = new List<ComponentHealth>();
        foreach (var probe in probes)
        {
            var state = await probe.VerificarAsync(cancellationToken);
            report.Add(new ComponentHealth(probe.Nome, state.Saudavel ? "healthy" : "unhealthy", state.Detalhe));
        }

        report.Add(await ReachAsync("zitadel", Jwks(configuration["Identity:Issuer"]), "Zitadel issuer is empty", cancellationToken));
        report.Add(await ReachAsync("cerbos", Cerbos(configuration["Authorization:Endpoint"]), "Cerbos endpoint is empty", cancellationToken));
        return report;
    }

    private static string? Jwks(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        return issuer.Trim().TrimEnd('/') + "/.well-known/openid-configuration";
    }

    private static string? Cerbos(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        return endpoint.Trim().TrimEnd('/') + "/_cerbos/health";
    }

    private static async Task<ComponentHealth> ReachAsync(string name, string? address, string emptyDetail, CancellationToken cancellationToken)
    {
        if (address is null)
        {
            return new ComponentHealth(name, "not-configured", emptyDetail);
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(address, cancellationToken);
            var status = response.IsSuccessStatusCode ? "healthy" : "unhealthy";
            return new ComponentHealth(name, status, ((int)response.StatusCode).ToString());
        }
        catch (HttpRequestException)
        {
            return new ComponentHealth(name, "unhealthy", "unreachable");
        }
        catch (TaskCanceledException)
        {
            return new ComponentHealth(name, "unhealthy", "unreachable");
        }
    }
}
