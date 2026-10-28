using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gatekeeper.Authorization;
using gatekeeper.Identity;
using gatekeeper.Identity.Adapters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace gatekeeper.Tests.Identity;

public class AccessAndPolicyTests
{
    [Fact]
    public async Task Admin_can_edit_and_soft_delete_while_support_cannot()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("App:Perfil", "teste"));
        using var client = factory.CreateClient();
        var owner = await Token(client, "/api/account/setup", new { email = "owner@localhost", password = "owner-pass", displayName = "Owner" });
        Authorize(client, owner);
        var organizationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/organizations/" + organizationId, new { name = "Local", host = "localhost", idpIssuer = "", directoryId = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/organizations/" + organizationId, new { name = "Local Plus", host = "localhost", idpIssuer = "", directoryId = "" })).StatusCode);

        var memberId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/members/" + memberId, new
        {
            organizationId,
            email = "support@localhost",
            displayName = "Support",
            role = "support",
            password = "support-pass",
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/organizations/" + organizationId)).StatusCode);
        var hidden = await client.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.Equal(0, hidden.GetArrayLength());

        using var supportClient = factory.CreateClient();
        Authorize(supportClient, await Token(supportClient, "/api/account/login", new { email = "support@localhost", password = "support-pass" }));
        var replacement = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await supportClient.PutAsJsonAsync("/api/organizations/" + replacement, new { name = "Nope", host = "nope.example", idpIssuer = "" })).StatusCode);
        var policies = await client.GetFromJsonAsync<JsonElement>("/api/policies");
        Assert.Contains(policies.EnumerateArray(), item => item.GetProperty("resource").GetString() == "policy" && item.GetProperty("role").GetString() == "admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await supportClient.DeleteAsync("/api/policies/" + policies[0].GetProperty("id").GetGuid())).StatusCode);
    }

    [Fact]
    public async Task Account_owner_cannot_open_the_console()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("App:Perfil", "teste"));
        using var client = factory.CreateClient();
        Authorize(client, await Token(client, "/api/account/setup", new { email = "admin@localhost", password = "admin-pass", displayName = "Admin" }));
        var organizationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/organizations/" + organizationId, new { name = "Local", host = "localhost", idpIssuer = "", directoryId = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/members/" + Guid.NewGuid(), new
        {
            organizationId,
            email = "customer@localhost",
            displayName = "Customer",
            role = "owner",
            password = "owner-pass",
        })).StatusCode);

        using var customer = factory.CreateClient();
        var response = await customer.PostAsJsonAsync("/api/account/login", new { email = "customer@localhost", password = "owner-pass" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("service company", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Zitadel_session_returns_the_project_role()
    {
        var handler = new ScriptedHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://idp.example") };
        var directory = new ZitadelAccountDirectory(client, new ZitadelSettings("https://idp.example", "pat", "project-1", "org-1"));

        var result = await directory.SignInAsync("ada@example.com", "secret", CancellationToken.None);

        Assert.Equal("authenticated", result.Status);
        Assert.Equal("user-1", result.Session!.Subject);
        Assert.Equal("owner", result.Session.Roles[0]);
        Assert.Contains("/v2/sessions", handler.Paths);
        Assert.Contains("/management/v1/users/grants/_search", handler.Paths);
    }

    [Fact]
    public void Passkey_registration_and_assertion_share_the_same_key()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var point = ecdsa.ExportParameters(false).Q;
        var challenge = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        var origin = "http://localhost:5173";
        var credentialId = RandomNumberGenerator.GetBytes(16);
        var registered = PasskeyCeremony.TryRegister(origin, challenge, Credential(challenge, origin, "webauthn.create", Attestation(origin, credentialId, point.X!, point.Y!), null, null), out var id, out var publicKey);

        Assert.True(registered);
        Assert.Equal(Base64Url.Encode(credentialId), id);
        var authenticator = new byte[37];
        var assertionChallenge = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        var client = Client(assertionChallenge, origin, "webauthn.get");
        var hash = SHA256.HashData(client);
        var signed = authenticator.Concat(hash).ToArray();
        var signature = ecdsa.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var verified = PasskeyCeremony.Verify(origin, assertionChallenge, publicKey, Credential(assertionChallenge, origin, "webauthn.get", null, authenticator, signature));

        Assert.True(verified);
    }

    private static async Task<string> Token(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, payload.ToString());
        return payload.GetProperty("token").GetString()!;
    }

    private static void Authorize(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static string Credential(string challenge, string origin, string type, byte[]? attestation, byte[]? authenticator, byte[]? signature)
    {
        var response = new Dictionary<string, string>
        {
            ["clientDataJSON"] = Base64Url.Encode(Client(challenge, origin, type)),
        };
        if (attestation is not null)
        {
            response["attestationObject"] = Base64Url.Encode(attestation);
        }

        if (authenticator is not null)
        {
            response["authenticatorData"] = Base64Url.Encode(authenticator);
        }

        if (signature is not null)
        {
            response["signature"] = Base64Url.Encode(signature);
        }

        return JsonSerializer.Serialize(new { response });
    }

    private static byte[] Client(string challenge, string origin, string type)
    {
        return Encoding.UTF8.GetBytes("{\"type\":\"" + type + "\",\"origin\":\"" + origin + "\",\"challenge\":\"" + challenge + "\"}");
    }

    private static byte[] Attestation(string origin, byte[] credentialId, byte[] x, byte[] y)
    {
        var host = new Uri(origin).Host;
        var auth = new List<byte>();
        auth.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(host)));
        auth.Add(0x41);
        auth.AddRange(new byte[4]);
        auth.AddRange(new byte[16]);
        auth.Add((byte)(credentialId.Length >> 8));
        auth.Add((byte)credentialId.Length);
        auth.AddRange(credentialId);
        auth.AddRange(Cose(x, y));
        var body = new List<byte>();
        body.Add(0xA3);
        Text(body, "fmt");
        Text(body, "none");
        Text(body, "authData");
        Bytes(body, auth.ToArray());
        Text(body, "attStmt");
        body.Add(0xA0);
        return body.ToArray();
    }

    private static byte[] Cose(byte[] x, byte[] y)
    {
        var body = new List<byte> { 0xA4, 0x01, 0x02, 0x03, 0x26, 0x21 };
        Bytes(body, x);
        body.Add(0x22);
        Bytes(body, y);
        return body.ToArray();
    }

    private static void Text(List<byte> body, string value)
    {
        body.Add((byte)(0x60 | value.Length));
        body.AddRange(Encoding.UTF8.GetBytes(value));
    }

    private static void Bytes(List<byte> body, byte[] value)
    {
        body.Add(0x58);
        body.Add((byte)value.Length);
        body.AddRange(value);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var body = path switch
            {
                "/v2/sessions" => "{\"sessionId\":\"session-1\",\"sessionToken\":\"token-1\"}",
                "/v2/sessions/session-1" => "{\"session\":{\"factors\":{\"user\":{\"id\":\"user-1\",\"organizationId\":\"org-1\"}}}}",
                "/v2/users/user-1/authentication_methods" => "{\"methods\":[]}",
                "/management/v1/users/grants/_search" => "{\"result\":[{\"roleKeys\":[\"owner\"]}]}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
