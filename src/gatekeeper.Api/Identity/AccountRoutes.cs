using gatekeeper.Api;
using gatekeeper.Billing.Http;
using gatekeeper.Common;
using gatekeeper.Governance;
using gatekeeper.Identity;

namespace gatekeeper.Identity.Http;

public sealed class AccountRoutes : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/api/account/status", Status).WithTags("Account");
        rotas.MapGet("/api/account/me", Me).WithTags("Account");
        rotas.MapPost("/api/account/setup", Setup).WithTags("Account");
        rotas.MapPost("/api/account/login", Login).WithTags("Account");
        rotas.MapPost("/api/account/mfa", Mfa).WithTags("Account");
        rotas.MapPost("/api/account/passkey/options", PasskeyOptions).WithTags("Account");
        rotas.MapPost("/api/account/passkey", Passkey).WithTags("Account");
        rotas.MapPost("/api/account/password-reset", RequestReset).WithTags("Account");
        rotas.MapPost("/api/account/password-reset/confirm", ConfirmReset).WithTags("Account");
        rotas.MapPost("/api/account/factors/mfa", BeginMfa).WithTags("Account");
        rotas.MapPost("/api/account/factors/mfa/confirm", EnableMfa).WithTags("Account");
        rotas.MapPost("/api/account/factors/passkey/options", BeginPasskey).WithTags("Account");
        rotas.MapPost("/api/account/factors/passkey", FinishPasskey).WithTags("Account");
    }

    private static IResult Status(IAccountDirectory directory)
    {
        var status = directory.Status();
        return Results.Ok(new { setupRequired = status.SetupRequired, provider = status.Provider });
    }

    private static IResult Me(HttpContext context, IAccountDirectory directory)
    {
        var principal = RequestPrincipal.Read(context);
        if (principal.Subject == "machine")
        {
            return Results.Json(new { detail = "sign in is required" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!Staff(principal.Roles))
        {
            return Results.Json(new { detail = "This console belongs to the service company." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var profile = directory.Profile(principal.Subject);
        return Results.Ok(new
        {
            subject = principal.Subject,
            organization = principal.Organization,
            roles = principal.Roles,
            email = profile?.Email ?? string.Empty,
            displayName = profile?.DisplayName ?? string.Empty,
        });
    }

    private static async Task<IResult> Setup(AccountBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.SetupAsync(body.Email ?? string.Empty, body.Password ?? string.Empty, body.DisplayName ?? string.Empty, cancellationToken), options, body.Email ?? string.Empty);
    }

    private static async Task<IResult> Login(AccountBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, LoginThrottle throttle, CancellationToken cancellationToken)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "local";
        if (!throttle.Allow(ip, body.Email ?? string.Empty, DateTimeOffset.UtcNow))
        {
            context.Response.Headers.RetryAfter = "300";
            return Results.Json(new { status = 429, detail = "rate limited" }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        return Answer(context, await directory.SignInAsync(body.Email ?? string.Empty, body.Password ?? string.Empty, cancellationToken), options, body.Email ?? string.Empty);
    }

    private static async Task<IResult> Mfa(FactorBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.ConfirmFactorAsync(body.Challenge ?? string.Empty, body.Code ?? string.Empty, cancellationToken), options);
    }

    private static async Task<IResult> PasskeyOptions(AccountBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.PasskeyOptionsAsync(body.Email ?? string.Empty, Origin(context), cancellationToken), options);
    }

    private static async Task<IResult> Passkey(PasskeyBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.ConfirmPasskeyAsync(body.Challenge ?? string.Empty, body.Credential ?? string.Empty, Origin(context), cancellationToken), options);
    }

    private static async Task<IResult> RequestReset(AccountBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.RequestResetAsync(body.Email ?? string.Empty, cancellationToken), options);
    }

    private static async Task<IResult> ConfirmReset(ResetBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.CompleteResetAsync(body.Email ?? string.Empty, body.Code ?? string.Empty, body.Password ?? string.Empty, cancellationToken), options);
    }

    private static async Task<IResult> BeginMfa(HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.BeginMfaAsync(RequestPrincipal.Read(context).Subject, cancellationToken), options);
    }

    private static async Task<IResult> EnableMfa(FactorBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.EnableMfaAsync(RequestPrincipal.Read(context).Subject, body.Code ?? string.Empty, cancellationToken), options);
    }

    private static async Task<IResult> BeginPasskey(HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.BeginPasskeyRegistrationAsync(RequestPrincipal.Read(context).Subject, Origin(context), cancellationToken), options);
    }

    private static async Task<IResult> FinishPasskey(PasskeyBody body, HttpContext context, IAccountDirectory directory, OpcoesDeAutenticacao options, CancellationToken cancellationToken)
    {
        return Answer(context, await directory.FinishPasskeyRegistrationAsync(RequestPrincipal.Read(context).Subject, body.Challenge ?? string.Empty, body.Credential ?? string.Empty, Origin(context), cancellationToken), options);
    }

    private static bool Staff(IReadOnlyList<string> roles)
    {
        foreach (var role in roles)
        {
            if (role == "admin" || role == "support" || role == "Staff.Admin" || role == "Staff.Support" || role == "Staff.Auditor")
            {
                return true;
            }
        }

        return false;
    }

    private static string Origin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0)
        {
            return origin;
        }

        return context.Request.Scheme + "://" + context.Request.Host.Value;
    }

    private static IResult Answer(HttpContext context, AccountResult result, OpcoesDeAutenticacao options, string email = "")
    {
        if (result.Status == "invalid")
        {
            return Results.Json(new { status = result.Status, detail = result.Detail }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (result.Status == "authenticated" && result.Session is not null)
        {
            if (!Staff(result.Session.Roles))
            {
                return Results.Json(new { status = "invalid", detail = "This console belongs to the service company. Account owners do not sign in here." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var token = ValidadorDeJwt.EmitirSessao(
                options.SessionIssuer,
                options.SessionAudience,
                options.SessionKey,
                DateTimeOffset.UtcNow.AddHours(8),
                result.Session.Subject,
                result.Session.Organization,
                result.Session.Roles,
                email);
            if (!token.Sucesso)
            {
                return RespostaHttp.De(token);
            }

            context.Response.Cookies.Append("gatekeeper_session", token.Valor, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.FromHours(8),
            });
            var csrf = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            context.Response.Cookies.Append("gatekeeper_csrf", csrf, new CookieOptions
            {
                HttpOnly = false,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.FromHours(8),
            });
            return Results.Ok(new { status = "authenticated", token = token.Valor, roles = result.Session.Roles, hint = result.Hint, detail = result.Detail });
        }

        return Results.Ok(new
        {
            status = result.Status,
            challenge = result.Challenge,
            hint = result.Hint,
            detail = result.Detail,
            options = result.Passkey is null ? null : new
            {
                challengeId = result.Challenge,
                challenge = result.Passkey.Challenge,
                rpId = result.Passkey.RpId,
                userId = result.Passkey.UserId,
                userName = result.Passkey.UserName,
                credentialIds = result.Passkey.CredentialIds,
            },
        });
    }
}

public sealed record AccountBody(string? Email, string? Password, string? DisplayName);

public sealed record FactorBody(string? Challenge, string? Code);

public sealed record PasskeyBody(string? Challenge, string? Credential);

public sealed record ResetBody(string? Email, string? Code, string? Password);
