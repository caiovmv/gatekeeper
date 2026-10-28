using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gatekeeper.Identity;
using Microsoft.Extensions.Logging;

namespace gatekeeper.Identity.Adapters;

public sealed class FlowableLoginSettings
{
    public FlowableLoginSettings(string clientId, string projectId, string redirectUri, string resetUrlTemplate, bool returnResetCode)
    {
        ClientId = clientId.Trim();
        ProjectId = projectId.Trim();
        RedirectUri = redirectUri.Trim();
        ResetUrlTemplate = resetUrlTemplate.Trim();
        ReturnResetCode = returnResetCode;
    }

    /// <summary>Public OIDC application (authorization code with PKCE) registered for the console.</summary>
    public string ClientId { get; }

    /// <summary>Project of the orchestrator API: its id goes into the token audience.</summary>
    public string ProjectId { get; }

    /// <summary>Redirect URI registered for the application. Only used to complete the code exchange.</summary>
    public string RedirectUri { get; }

    /// <summary>Where the e-mailed link points. Placeholders: {{.UserID}}, {{.Code}} and {{.OrgID}}.</summary>
    public string ResetUrlTemplate { get; }

    /// <summary>Hand the reset code back to the caller instead of e-mailing it. Local development only.</summary>
    public bool ReturnResetCode { get; }
}

/// <summary>
/// Headless sign-in for the orchestrator console. The browser never visits the identity provider: this service runs the
/// authorization-code flow with PKCE on the person's behalf. It starts an auth request, checks the password in a
/// session, finalizes the auth request with that session and exchanges the code for the access token.
/// The HttpClient must not follow redirects (the auth request id comes from the redirect itself).
/// </summary>
public sealed class ZitadelFlowableLogin : IFlowableLogin
{
    private readonly HttpClient _client;
    private readonly ZitadelSettings _settings;
    private readonly FlowableLoginSettings _flowable;
    private readonly ILogger? _log;

    public ZitadelFlowableLogin(HttpClient client, ZitadelSettings settings, FlowableLoginSettings flowable, ILogger? log = null)
    {
        _client = client;
        _settings = settings;
        _flowable = flowable;
        _log = log;
    }

    public async Task<FlowableLoginResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return Invalid("Informe o e-mail e a senha.");
        }

        var verifier = Random(48);
        var state = Random(16);
        var authRequestId = await StartAuthRequest(verifier, state, cancellationToken);
        if (authRequestId.Length == 0)
        {
            return new FlowableLoginResult("unavailable", string.Empty, 0, "O serviço de login não respondeu. Tente de novo em instantes; se continuar, avise o administrador.");
        }

        var sessionFailure = new Failure();
        var session = await PostAsync("/v2/sessions", new
        {
            checks = new
            {
                user = new { loginName = email.Trim() },
                password = new { password },
            },
        }, true, cancellationToken, sessionFailure);
        if (session is null)
        {
            return ExplainSessionFailure(sessionFailure);
        }

        var sessionId = Text(session.Value, "sessionId");
        var sessionToken = Text(session.Value, "sessionToken");
        if (sessionId.Length == 0 || sessionToken.Length == 0)
        {
            return Invalid("E-mail ou senha incorretos.");
        }

        if (await RequiresSecondFactor(sessionId, sessionToken, cancellationToken))
        {
            return new FlowableLoginResult("mfa", string.Empty, 0, "Esta conta exige um segundo fator, que este login ainda não suporta.");
        }

        var finishFailure = new Failure();
        var finalized = await PostAsync("/v2/oidc/auth_requests/" + Uri.EscapeDataString(authRequestId), new
        {
            session = new { sessionId, sessionToken },
        }, true, cancellationToken, finishFailure);
        var callback = finalized is null ? string.Empty : Text(finalized.Value, "callbackUrl");
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var callbackUri))
        {
            return ExplainFinishFailure(finishFailure);
        }

        var query = ParseQuery(callbackUri.Query);
        if (!query.TryGetValue("code", out var code) || code.Length == 0 || !query.TryGetValue("state", out var returnedState) || !string.Equals(returnedState, state, StringComparison.Ordinal))
        {
            return Invalid("O retorno do login não confere com o pedido.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/v2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _flowable.RedirectUri,
                ["client_id"] = _flowable.ClientId,
                ["code_verifier"] = verifier,
            }),
        };
        var tokenFailure = new Failure();
        var token = await SendAsync(request, cancellationToken, tokenFailure);
        var accessToken = token is null ? string.Empty : Text(token.Value, "access_token");
        if (accessToken.Length == 0)
        {
            return ExplainTokenFailure(tokenFailure);
        }

        var expires = token!.Value.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600;
        return new FlowableLoginResult("authenticated", accessToken, expires, string.Empty);
    }

    public async Task<FlowableSignUpResult> SignUpAsync(string email, string password, string displayName, CancellationToken cancellationToken)
    {
        var address = email.Trim();
        var name = displayName.Trim();
        if (!address.Contains('@', StringComparison.Ordinal) || string.IsNullOrEmpty(password) || name.Length == 0)
        {
            return new FlowableSignUpResult("invalid", "Informe nome, e-mail e senha.");
        }

        var space = name.IndexOf(' ', StringComparison.Ordinal);
        var given = space < 0 ? name : name[..space];
        var family = space < 0 ? "-" : name[(space + 1)..].Trim();
        var created = await PostAsync("/v2/users/human", new
        {
            username = address,
            organization = new { orgId = _settings.OrganizationId },
            profile = new { givenName = given, familyName = family, displayName = name },
            email = new { email = address, isVerified = _flowable.ReturnResetCode },
            password = new { password, changeRequired = false },
        }, true, cancellationToken);
        return created is not null && Text(created.Value, "userId").Length > 0
            ? new FlowableSignUpResult("created", "Conta criada. Ela ainda não tem acesso ao Flowable: peça a um administrador para conceder um papel.")
            : new FlowableSignUpResult("invalid", "Não foi possível criar a conta: ela já existe ou a senha não atende à política.");
    }

    public async Task<FlowableResetRequest> RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        const string generic = "Se a conta existir, enviamos as instruções para o e-mail cadastrado.";
        var userId = await UserId(email, cancellationToken);
        if (userId.Length == 0)
        {
            return new FlowableResetRequest(true, string.Empty, generic);
        }

        object body = _flowable.ReturnResetCode
            ? new { returnCode = new { } }
            : new { sendLink = new { notificationType = "NOTIFICATION_TYPE_Email", urlTemplate = _flowable.ResetUrlTemplate } };
        var response = await PostAsync("/v2/users/" + userId + "/password_reset", body, true, cancellationToken);
        var code = _flowable.ReturnResetCode && response is not null ? Text(response.Value, "verificationCode") : string.Empty;
        return new FlowableResetRequest(true, code, generic);
    }

    public async Task<bool> CompleteResetAsync(string userId, string email, string code, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var id = string.IsNullOrWhiteSpace(userId) ? await UserId(email, cancellationToken) : userId.Trim();
        if (id.Length == 0)
        {
            return false;
        }

        var response = await PostAsync("/v2/users/" + Uri.EscapeDataString(id) + "/password", new
        {
            verificationCode = code.Trim(),
            newPassword = new { password, changeRequired = false },
        }, true, cancellationToken);
        return response is not null;
    }

    private async Task<string> StartAuthRequest(string verifier, string state, CancellationToken cancellationToken)
    {
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var scope = string.Join(' ', "openid", "profile", "email", "urn:zitadel:iam:org:project:id:" + _flowable.ProjectId + ":aud", "urn:zitadel:iam:org:projects:roles");
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _flowable.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _flowable.RedirectUri,
            ["scope"] = scope,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        var url = "/oauth/v2/authorize?" + string.Join('&', query.Select(par => Uri.EscapeDataString(par.Key) + "=" + Uri.EscapeDataString(par.Value)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _client.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is null)
            {
                return string.Empty;
            }

            var location = response.Headers.Location.OriginalString;
            var marker = location.IndexOf('?', StringComparison.Ordinal);
            var parameters = marker < 0 ? new Dictionary<string, string>() : ParseQuery(location[(marker + 1)..]);
            var id = parameters.TryGetValue("authRequest", out var direct) ? direct : parameters.TryGetValue("requestId", out var viaRequest) ? viaRequest : string.Empty;
            return id.StartsWith("oidc_", StringComparison.Ordinal) ? id["oidc_".Length..] : id;
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
        catch (TaskCanceledException)
        {
            return string.Empty;
        }
    }

    private async Task<bool> RequiresSecondFactor(string sessionId, string sessionToken, CancellationToken cancellationToken)
    {
        var session = await GetAsync("/v2/sessions/" + sessionId + "?sessionToken=" + Uri.EscapeDataString(sessionToken), cancellationToken);
        var userId = session is null ? string.Empty : Nested(session.Value, "session", "factors", "user", "id");
        if (userId.Length == 0)
        {
            return false;
        }

        var methods = await GetAsync("/v2/users/" + userId + "/authentication_methods", cancellationToken);
        return methods is not null && methods.Value.GetRawText().Contains("AUTHENTICATION_METHOD_TYPE_TOTP", StringComparison.Ordinal);
    }

    private async Task<string> UserId(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var found = await PostAsync("/v2/users", new
        {
            queries = new[] { new { loginNameQuery = new { loginName = email.Trim(), method = "TEXT_QUERY_METHOD_EQUALS" } } },
        }, true, cancellationToken);
        if (found is null || !found.Value.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        return Text(result[0], "userId");
    }

    private async Task<JsonElement?> PostAsync(string path, object body, bool authorized, CancellationToken cancellationToken, Failure? failure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        Authorize(request, authorized);
        return await SendAsync(request, cancellationToken, failure);
    }

    private async Task<JsonElement?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Authorize(request, true);
        return await SendAsync(request, cancellationToken);
    }

    private void Authorize(HttpRequestMessage request, bool authorized)
    {
        if (authorized && _settings.ServiceToken.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _settings.ServiceToken);
        }
    }

    private async Task<JsonElement?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, Failure? failure = null)
    {
        try
        {
            using var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await LogFailure(request, response, cancellationToken);
                failure?.Set((int)response.StatusCode, detail);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (HttpRequestException)
        {
            failure?.Set(0, string.Empty);
            return null;
        }
        catch (TaskCanceledException)
        {
            failure?.Set(0, string.Empty);
            return null;
        }
        catch (JsonException)
        {
            failure?.Set(0, string.Empty);
            return null;
        }
    }

    /// <summary>Records which step the identity provider refused and its own error message (never the request body).</summary>
    private async Task<string> LogFailure(HttpRequestMessage request, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            body = string.Empty;
        }

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var shortBody = body.Length > 400 ? body[..400] : body;
        _log?.LogWarning("Flowable login: {Method} {Path} -> {Status}: {Detail}", request.Method, path, (int)response.StatusCode, shortBody);
        return shortBody;
    }

    /// <summary>What the identity provider answered when a step failed. Used only to choose the message for the person.</summary>
    private sealed class Failure
    {
        public int Status { get; private set; }

        public string Detail { get; private set; } = string.Empty;

        public void Set(int status, string detail)
        {
            Status = status;
            Detail = detail;
        }

        public bool Mentions(params string[] fragments)
        {
            return fragments.Any(fragment => Detail.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static FlowableLoginResult ExplainSessionFailure(Failure failure)
    {
        if (failure.Status == 0)
        {
            return Unavailable("Não foi possível falar com o serviço de identidade. Verifique a conexão e tente de novo; se continuar, avise o administrador.");
        }

        if (failure.Status is 401 or 403 || failure.Mentions("permission", "unauthenticated", "invalid token"))
        {
            return Unavailable("O serviço de login perdeu a credencial para falar com o provedor de identidade. Avise o administrador.");
        }

        if (failure.Mentions("locked"))
        {
            return Denied("Esta conta está bloqueada, em geral por tentativas de senha erradas. Peça a um administrador para desbloqueá-la.");
        }

        if (failure.Mentions("inactive", "deactivated", "not active"))
        {
            return Denied("Esta conta está desativada. Peça a um administrador para reativá-la.");
        }

        // Usuário inexistente e senha errada recebem a mesma resposta, para não revelar quais e-mails têm conta.
        return Invalid("E-mail ou senha incorretos. Confira os dados ou use \"Esqueci a senha\".");
    }

    private static FlowableLoginResult ExplainFinishFailure(Failure failure)
    {
        if (failure.Status == 0)
        {
            return Unavailable("O login ficou pela metade porque o serviço de identidade parou de responder. Tente de novo.");
        }

        if (failure.Mentions("AUTH-AWfge", "No matching permissions") || failure.Status is 401 or 403)
        {
            return Unavailable("O servidor de login não tem permissão para concluir o acesso no provedor de identidade. Avise o administrador: falta conceder o papel de login ao serviço (zitadel/provisionar.sh).");
        }

        if (failure.Mentions("grant", "role", "not granted", "no access"))
        {
            return Denied("Sua senha está certa, mas esta conta ainda não tem acesso ao Flowable. Peça a um administrador para conceder um papel (por exemplo, modelador).");
        }

        if (failure.Mentions("expired", "not found", "already"))
        {
            return Invalid("O pedido de login expirou. Tente entrar de novo.");
        }

        return Unavailable("O provedor de identidade não concluiu o seu login. Tente de novo e, se continuar, avise o administrador informando o horário.");
    }

    private static FlowableLoginResult ExplainTokenFailure(Failure failure)
    {
        if (failure.Status == 0)
        {
            return Unavailable("O login ficou pela metade porque o serviço de identidade parou de responder. Tente de novo.");
        }

        if (failure.Mentions("grant", "role", "access_denied", "no access"))
        {
            return Denied("Sua senha está certa, mas esta conta ainda não tem acesso ao Flowable. Peça a um administrador para conceder um papel (por exemplo, modelador).");
        }

        return Unavailable("O provedor de identidade não entregou o acesso. Tente de novo e, se continuar, avise o administrador informando o horário.");
    }

    private static FlowableLoginResult Unavailable(string detail)
    {
        return new FlowableLoginResult("unavailable", string.Empty, 0, detail);
    }

    private static FlowableLoginResult Denied(string detail)
    {
        return new FlowableLoginResult("denied", string.Empty, 0, detail);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var key = WebUtility.UrlDecode(separator < 0 ? part : part[..separator]);
            values[key] = separator < 0 ? string.Empty : WebUtility.UrlDecode(part[(separator + 1)..]);
        }

        return values;
    }

    private static string Random(int bytes)
    {
        return Base64Url(RandomNumberGenerator.GetBytes(bytes));
    }

    private static string Base64Url(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Text(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string Nested(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next))
            {
                return string.Empty;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }

    private static FlowableLoginResult Invalid(string detail)
    {
        return new FlowableLoginResult("invalid", string.Empty, 0, detail);
    }
}
