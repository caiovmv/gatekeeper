using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gatekeeper.Api;
using gatekeeper.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace gatekeeper.Tests.Identity;

public class PlatformIdentityTests
{
    private const string Eco = "/api/esqueleto/eco";

    private const string Issuer = "https://idp.example";

    private const string Audience = "gatekeeper-api";

    [Fact]
    public async Task Empty_issuer_keeps_the_voice_edge_on_the_machine_credential()
    {
        await using var open = new WebApplicationFactory<Program>();
        using var openClient = open.CreateClient();
        Assert.Equal(string.Empty, open.Services.GetRequiredService<OpcoesDeAutenticacao>().PlatformIssuer);
        Assert.Equal(HttpStatusCode.OK, await Get(openClient, null, null));

        await using var keyed = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Auth:ApiKey", "secret"));
        using var keyedClient = keyed.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(keyedClient, null, null));
        Assert.Equal(HttpStatusCode.OK, await Get(keyedClient, null, "secret"));
        Assert.True(keyed.Services.GetRequiredService<IMachineCredential>().Matches("secret", "secret"));
    }

    [Fact]
    public async Task Zitadel_token_is_accepted_and_a_bad_bearer_does_not_fall_through_to_the_api_key()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var modulus = Base64Url.Encode(parameters.Modulus!);
        var exponent = Base64Url.Encode(parameters.Exponent!);
        await using var factory = Factory(modulus, exponent, null);
        using var client = factory.CreateClient();
        var token = Sign(rsa, Issuer, Audience, "user-1", "org-1", new[] { "admin" }, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(HttpStatusCode.OK, await Get(client, token, null));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(client, token + "x", "secret"));
        Assert.Equal(HttpStatusCode.OK, await Get(client, null, "secret"));
    }

    [Fact]
    public async Task Host_that_names_another_issuer_rejects_the_token()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        await using var factory = Factory(
            Base64Url.Encode(parameters.Modulus!),
            Base64Url.Encode(parameters.Exponent!),
            "https://other.example");
        using var client = factory.CreateClient();
        var token = Sign(rsa, Issuer, Audience, "user-1", "org-1", new[] { "admin" }, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, await Get(client, token, null));
    }

    [Fact]
    public void Jwks_document_exposes_the_rsa_key()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var json = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"test\",\"n\":\""
            + Base64Url.Encode(parameters.Modulus!)
            + "\",\"e\":\""
            + Base64Url.Encode(parameters.Exponent!)
            + "\"}]}";
        var keys = JwksDocument.Read(json);

        Assert.Single(keys);
        Assert.Equal("test", keys[0].KeyId);
    }

    private static WebApplicationFactory<Program> Factory(string modulus, string exponent, string? hostIssuer)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Auth:ApiKey", "secret");
                builder.UseSetting("Identity:Issuer", Issuer);
                builder.UseSetting("Identity:Audience", Audience);
                builder.UseSetting("Identity:KeyModulus", modulus);
                builder.UseSetting("Identity:KeyExponent", exponent);
                if (hostIssuer is not null)
                {
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<IOrganizationDirectory>(new FixedDirectory(hostIssuer));
                    });
                }
            });
    }

    private static async Task<HttpStatusCode> Get(HttpClient client, string? bearer, string? apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Eco);
        if (bearer is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        }

        if (apiKey is not null)
        {
            request.Headers.Add(AutenticacaoPorChave.Cabecalho, apiKey);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string Sign(RSA rsa, string issuer, string audience, string subject, string organization, string[] roles, DateTimeOffset expiry)
    {
        var header = Base64Url.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\",\"kid\":\"test\"}"));
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["sub"] = subject,
            ["exp"] = expiry.ToUnixTimeSeconds(),
            ["urn:zitadel:iam:org:id"] = organization,
            ["urn:zitadel:iam:org:project:roles"] = roles.ToDictionary(role => role, _ => new { }),
        });
        var body = Base64Url.Encode(Encoding.UTF8.GetBytes(payload));
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(header + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + body + "." + Base64Url.Encode(signature);
    }

    private sealed class FixedDirectory : IOrganizationDirectory
    {
        private readonly string _issuer;

        public FixedDirectory(string issuer)
        {
            _issuer = issuer;
        }

        public string? IssuerForHost(string host)
        {
            return _issuer;
        }
    }
}
