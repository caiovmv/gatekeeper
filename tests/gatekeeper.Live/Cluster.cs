using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace gatekeeper.Live;

public static class LiveAccounts
{
    public static string StaffEmail
    {
        get { return Environment.GetEnvironmentVariable("GATEKEEPER_STAFF_EMAIL") ?? "staff@gatekeeper.test"; }
    }

    public static string StaffPassword
    {
        get { return Environment.GetEnvironmentVariable("GATEKEEPER_STAFF_PASSWORD") ?? "Staff-pass-1"; }
    }

    public static string SupportPassword
    {
        get { return Environment.GetEnvironmentVariable("GATEKEEPER_SUPPORT_PASSWORD") ?? "Support-pass-1"; }
    }
}

public static class Cluster
{
    public static string BaseUrl()
    {
        return Environment.GetEnvironmentVariable("GATEKEEPER_LIVE_URL") ?? string.Empty;
    }

    public static HttpClient Open()
    {
        var url = BaseUrl().Trim();
        if (url.Length == 0)
        {
            throw new InvalidOperationException("GATEKEEPER_LIVE_URL is empty");
        }

        return new HttpClient
        {
            BaseAddress = new Uri(url.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(40),
        };
    }

    public static async Task<string> Login(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("api/account/login", new { email, password });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("login failed " + (int)response.StatusCode + " " + body);
        }

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("token").GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("login did not return a session");
        }

        return token;
    }

    public static void Authorize(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

[CollectionDefinition("live", DisableParallelization = true)]
public sealed class LiveCollection : ICollectionFixture<StaffSession>
{
}

public sealed class StaffSession : IAsyncLifetime
{
    public HttpClient Client { get; private set; } = new();

    public string Token { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (Cluster.BaseUrl().Length == 0)
        {
            return;
        }

        Client.Dispose();
        Client = Cluster.Open();
        Token = await Cluster.Login(Client, LiveAccounts.StaffEmail, LiveAccounts.StaffPassword);
        Cluster.Authorize(Client, Token);
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }
}
