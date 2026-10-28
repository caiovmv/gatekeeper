using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using gatekeeper.Api;
using gatekeeper.Governance;
using gatekeeper.Identity;

namespace gatekeeper.Governance.Http;

public sealed class FederationRoutes : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/api/account/oidc/callback", OidcCallback).WithTags("Account");
        rotas.MapPost("/api/account/saml/acs", SamlAcs).WithTags("Account");
        rotas.MapPost("/api/account/logout", Logout).WithTags("Account");
    }

    private static async Task<IResult> OidcCallback(OidcBody body, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var outcome = OidcChecks.Validate(new OidcLogin(
            body.Code ?? string.Empty,
            body.State ?? string.Empty,
            body.ExpectedState ?? string.Empty,
            body.Nonce ?? string.Empty,
            body.ExpectedNonce ?? string.Empty,
            body.Issuer ?? string.Empty,
            body.ExpectedIssuer ?? string.Empty,
            body.Audience ?? string.Empty,
            body.ExpectedAudience ?? string.Empty,
            body.Method ?? string.Empty,
            body.ExpiresAt ?? now,
            now,
            body.StaffIssuer,
            body.IdentityType ?? "user"));
        if (!outcome.Ok)
        {
            GovernanceMeters.AuthorizationDenied.Add(1);
            return Results.Json(new { status = 401, detail = outcome.Detail }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var endpoint = configuration["Identity:OidcTokenEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Results.Json(new { status = 403, detail = "oidc code exchange is not a password session" }, statusCode: StatusCodes.Status403Forbidden);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = body.Code ?? string.Empty,
            ["code_verifier"] = body.CodeVerifier ?? string.Empty,
            ["client_id"] = configuration["Identity:OidcClientId"] ?? string.Empty,
            ["redirect_uri"] = configuration["Identity:OidcRedirectUri"] ?? string.Empty,
        });
        using var response = await client.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            GovernanceMeters.AuthorizationDenied.Add(1);
            return Results.Json(new { status = 403, detail = "oidc exchange failed" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Json(new { status = 403, detail = "assertion checked, identity link required" }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult SamlAcs(SamlBody body, IConfiguration configuration)
    {
        var pinned = configuration["Identity:SamlCertificate"] ?? string.Empty;
        if (pinned.Trim().Length == 0)
        {
            GovernanceMeters.AuthorizationDenied.Add(1);
            return Results.Json(new { status = 403, detail = "saml is closed" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var outcome = SamlChecks.Validate(
            body.Xml ?? string.Empty,
            body.Audience ?? string.Empty,
            body.InResponseTo ?? string.Empty,
            body.Destination ?? string.Empty,
            body.Recipient ?? string.Empty,
            pinned);
        if (!outcome.Ok)
        {
            GovernanceMeters.AuthorizationDenied.Add(1);
            return Results.Json(new { status = 401, detail = outcome.Detail }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!SignatureHolds(body.Xml ?? string.Empty, pinned))
        {
            GovernanceMeters.AuthorizationDenied.Add(1);
            return Results.Json(new { status = 401, detail = "signature rejected" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Json(new { status = 403, detail = "assertion checked, identity link required" }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult Logout(HttpContext context, SessionClock clock)
    {
        var presented = Presented(context);
        if (presented.Length > 0)
        {
            clock.RevokeToken(presented);
        }

        if (context.Items[AutenticacaoPorChave.PrincipalItem] is PlatformPrincipal principal)
        {
            clock.Forget(principal.Subject);
        }

        context.Response.Cookies.Delete("gatekeeper_session", new CookieOptions { Path = "/" });
        context.Response.Cookies.Delete("gatekeeper_csrf", new CookieOptions { Path = "/" });
        return Results.Ok(new { status = "signed-out" });
    }

    private static string Presented(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return header.Substring(prefix.Length).Trim();
        }

        if (context.Request.Cookies.TryGetValue("gatekeeper_session", out var cookie) && !string.IsNullOrWhiteSpace(cookie))
        {
            return cookie;
        }

        return string.Empty;
    }

    private static bool SignatureHolds(string xml, string certificateText)
    {
        try
        {
            var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificateText));
            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(xml);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            if (document.SelectSingleNode("//ds:Signature", manager) is not XmlElement node)
            {
                return false;
            }

            var signed = new SignedXml(document);
            signed.LoadXml(node);
            return signed.CheckSignature(cert, true);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed record OidcBody(string? Code, string? State, string? ExpectedState, string? Nonce, string? ExpectedNonce, string? Issuer, string? ExpectedIssuer, string? Audience, string? ExpectedAudience, string? Method, DateTimeOffset? ExpiresAt, bool StaffIssuer, string? IdentityType, string? CodeVerifier);

public sealed record SamlBody(string? Xml, string? Audience, string? InResponseTo, string? Destination, string? Recipient, string? Certificate);
