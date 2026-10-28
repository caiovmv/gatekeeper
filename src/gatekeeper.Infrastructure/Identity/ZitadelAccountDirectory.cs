using System.Net.Http.Json;
using System.Text.Json;
using gatekeeper.Identity;

namespace gatekeeper.Identity.Adapters;

public sealed class ZitadelSettings
{
    public ZitadelSettings(string issuer, string serviceToken, string projectId, string organizationId)
    {
        Issuer = issuer.Trim().TrimEnd('/');
        ServiceToken = serviceToken.Trim();
        ProjectId = projectId.Trim();
        OrganizationId = organizationId.Trim();
    }

    public string Issuer { get; }

    public string ServiceToken { get; }

    public string ProjectId { get; }

    public string OrganizationId { get; }
}

public sealed class ZitadelAccountDirectory : IAccountDirectory
{
    private readonly HttpClient _client;
    private readonly ZitadelSettings _settings;
    private readonly List<Pending> _pending = new();

    public ZitadelAccountDirectory(HttpClient client, ZitadelSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public AccountStatus Status()
    {
        return new AccountStatus(false, "zitadel");
    }

    public Task<AccountResult> SetupAsync(string email, string password, string displayName, CancellationToken cancellationToken)
    {
        return Task.FromResult(Invalid("The identity provider owns the first account."));
    }

    public async Task<AccountResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var created = await PostAsync("/v2/sessions", new
        {
            checks = new
            {
                user = new { loginName = email.Trim() },
                password = new { password },
            },
        }, true, cancellationToken);
        if (created is null)
        {
            return Invalid("credentials were rejected");
        }

        var sessionId = Text(created.Value, "sessionId");
        var sessionToken = Text(created.Value, "sessionToken");
        var session = await GetAsync("/v2/sessions/" + sessionId + "?sessionToken=" + Uri.EscapeDataString(sessionToken), true, cancellationToken);
        var userId = Nested(session, "session", "factors", "user", "id");
        if (userId.Length == 0)
        {
            return Invalid("the identity provider did not return a session");
        }

        if (await RequiresTotp(userId, session, cancellationToken))
        {
            return Hold(userId, "mfa", sessionId + ":" + sessionToken, null);
        }

        return await Authenticated(userId, Nested(session, "session", "factors", "user", "organizationId"), cancellationToken);
    }

    public async Task<AccountResult> ConfirmFactorAsync(string challenge, string code, CancellationToken cancellationToken)
    {
        var pending = Take(challenge, "mfa");
        if (pending is null)
        {
            return Invalid("the code was rejected");
        }

        var parts = pending.Secret.Split(':', 2);
        var updated = await PatchAsync("/v2/sessions/" + parts[0], new
        {
            sessionToken = parts.Length == 2 ? parts[1] : string.Empty,
            checks = new { totp = new { code } },
        }, cancellationToken);
        if (updated is null)
        {
            return Invalid("the code was rejected");
        }

        return await Authenticated(pending.UserId, string.Empty, cancellationToken);
    }

    public async Task<AccountResult> PasskeyOptionsAsync(string email, string origin, CancellationToken cancellationToken)
    {
        var host = Host(origin);
        var created = await PostAsync("/v2/sessions", new
        {
            checks = new { user = new { loginName = email.Trim() } },
            challenges = new
            {
                webAuthN = new
                {
                    domain = host,
                    userVerificationRequirement = "USER_VERIFICATION_REQUIREMENT_PREFERRED",
                },
            },
        }, true, cancellationToken);
        if (created is null)
        {
            return Invalid("no passkey is registered");
        }

        var sessionId = Text(created.Value, "sessionId");
        var sessionToken = Text(created.Value, "sessionToken");
        var userId = await UserId(email, cancellationToken);
        var challenge = Nested(created, "challenges", "webAuthN", "publicKeyCredentialRequestOptions", "challenge");
        var passkey = new PasskeyChallenge(string.Empty, challenge, host, userId, email.Trim(), Array.Empty<string>());
        return Hold(userId.Length == 0 ? email.Trim() : userId, "passkey", sessionId + ":" + sessionToken + ":" + challenge, passkey);
    }

    public async Task<AccountResult> ConfirmPasskeyAsync(string challenge, string credential, string origin, CancellationToken cancellationToken)
    {
        var pending = Take(challenge, "passkey");
        if (pending is null)
        {
            return Invalid("the passkey was rejected");
        }

        var parts = pending.Secret.Split(':', 3);
        var updated = await PatchAsync("/v2/sessions/" + parts[0], new
        {
            sessionToken = parts.Length > 1 ? parts[1] : string.Empty,
            checks = new
            {
                webAuthN = new
                {
                    credentialAssertionData = JsonSerializer.Deserialize<JsonElement>(credential),
                },
            },
        }, cancellationToken);
        if (updated is null)
        {
            return Invalid("the passkey was rejected");
        }

        return await Authenticated(pending.UserId, string.Empty, cancellationToken);
    }

    public async Task<AccountResult> RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        var userId = await UserId(email, cancellationToken);
        if (userId.Length == 0)
        {
            return new AccountResult("reset", null, string.Empty, null, string.Empty, "If the account exists, the identity provider accepted the reset.");
        }

        var response = await PostAsync("/v2/users/" + userId + "/password_reset", new { returnCode = new { } }, true, cancellationToken);
        var code = response is null ? string.Empty : Text(response.Value, "verificationCode");
        return new AccountResult("reset", null, string.Empty, null, code, "Enter the code on this screen.");
    }

    public async Task<AccountResult> CompleteResetAsync(string email, string code, string password, CancellationToken cancellationToken)
    {
        var userId = await UserId(email, cancellationToken);
        var response = userId.Length == 0
            ? null
            : await PostAsync("/v2/users/" + userId + "/password", new
            {
                verificationCode = code.Trim(),
                newPassword = new { password, changeRequired = false },
            }, true, cancellationToken);
        if (response is null)
        {
            return Invalid("the reset was rejected");
        }

        return await Authenticated(userId, string.Empty, cancellationToken);
    }

    public Task<AccountResult> BeginMfaAsync(string subject, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AccountResult("enroll", null, string.Empty, null, string.Empty, "Register the authenticator in the identity provider, then enter a code on the next sign-in."));
    }

    public Task<AccountResult> EnableMfaAsync(string subject, string code, CancellationToken cancellationToken)
    {
        return Task.FromResult(Invalid("The identity provider confirms the authenticator during sign-in."));
    }

    public Task<AccountResult> BeginPasskeyRegistrationAsync(string subject, string origin, CancellationToken cancellationToken)
    {
        return Task.FromResult(Invalid("Register the passkey from the sign-in screen of this console."));
    }

    public Task<AccountResult> FinishPasskeyRegistrationAsync(string subject, string challenge, string credential, string origin, CancellationToken cancellationToken)
    {
        return Task.FromResult(Invalid("Register the passkey from the sign-in screen of this console."));
    }

    public async Task<AccountResult> ProvisionAsync(string email, string displayName, string role, Guid organizationId, string password, CancellationToken cancellationToken)
    {
        if (_settings.ProjectId.Length == 0 || _settings.ServiceToken.Length == 0)
        {
            return Invalid("Identity:ServiceToken and Identity:ProjectId are required");
        }

        var organization = _settings.OrganizationId;
        var existing = await UserId(email, cancellationToken);
        var userId = existing;
        if (userId.Length == 0)
        {
            var created = await PostAsync("/v2/users/human", new
            {
                username = email.Trim(),
                organizationId = organization,
                profile = new { givenName = displayName.Trim(), familyName = displayName.Trim(), displayName = displayName.Trim() },
                email = new { email = email.Trim(), isVerified = true },
                password = new { password, changeRequired = false },
            }, true, cancellationToken);
            userId = created is null ? string.Empty : Text(created.Value, "userId");
        }

        if (userId.Length == 0)
        {
            return Invalid("the identity provider rejected the account");
        }

        var grant = await PostAsync("/management/v1/users/" + userId + "/grants", new
        {
            projectId = _settings.ProjectId,
            roleKeys = new[] { role },
        }, true, cancellationToken);
        if (grant is null)
        {
            return Invalid("the identity provider rejected the role");
        }

        return new AccountResult("authenticated", new AccountSession(userId, organizationId.ToString(), new[] { role }), string.Empty, null, string.Empty, string.Empty);
    }

    public async Task RetireAsync(string email, CancellationToken cancellationToken)
    {
        var userId = await UserId(email, cancellationToken);
        if (userId.Length > 0)
        {
            await PostAsync("/v2/users/" + userId + "/deactivate", new { }, true, cancellationToken);
        }
    }

    public AccountSession? Find(string subject)
    {
        return null;
    }

    public AccountProfile? Profile(string subject)
    {
        return null;
    }

    private async Task<bool> RequiresTotp(string userId, JsonElement? session, CancellationToken cancellationToken)
    {
        if (session is not null && Nested(session, "session", "factors", "totp", "verifiedAt").Length > 0)
        {
            return false;
        }

        var methods = await GetAsync("/v2/users/" + userId + "/authentication_methods", true, cancellationToken);
        if (methods is null)
        {
            return false;
        }

        return methods.Value.GetRawText().Contains("AUTHENTICATION_METHOD_TYPE_TOTP", StringComparison.Ordinal);
    }

    private async Task<AccountResult> Authenticated(string userId, string organization, CancellationToken cancellationToken)
    {
        if (_settings.ServiceToken.Length == 0)
        {
            return Invalid("Identity:ServiceToken is required to read roles");
        }

        var roles = await Roles(userId, cancellationToken);
        if (roles.Count == 0)
        {
            return Invalid("the account has no role on the project");
        }

        return new AccountResult("authenticated", new AccountSession(userId, organization, roles), string.Empty, null, string.Empty, string.Empty);
    }

    private async Task<IReadOnlyList<string>> Roles(string userId, CancellationToken cancellationToken)
    {
        var response = await PostAsync("/management/v1/users/grants/_search", new
        {
            queries = new[] { new { userIdQuery = new { userId } } },
        }, true, cancellationToken);
        var roles = new List<string>();
        if (response is null || !response.Value.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return roles;
        }

        foreach (var grant in result.EnumerateArray())
        {
            if (!grant.TryGetProperty("roleKeys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind == JsonValueKind.String && key.GetString() is string role && role.Length > 0 && !roles.Contains(role))
                {
                    roles.Add(role);
                }
            }
        }

        return roles;
    }

    private async Task<string> UserId(string email, CancellationToken cancellationToken)
    {
        var response = await PostAsync("/v2/users", new
        {
            queries = new[]
            {
                new { emailQuery = new { emailAddress = email.Trim(), method = "TEXT_QUERY_METHOD_EQUALS" } },
            },
        }, true, cancellationToken);
        if (response is null || !response.Value.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var user in result.EnumerateArray())
        {
            var id = Text(user, "userId");
            if (id.Length == 0)
            {
                id = Text(user, "id");
            }

            if (id.Length > 0)
            {
                return id;
            }
        }

        return string.Empty;
    }

    private AccountResult Hold(string userId, string purpose, string secret, PasskeyChallenge? passkey)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (_pending)
        {
            _pending.Add(new Pending(id, userId, purpose, secret, DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        var announced = passkey is null ? null : passkey with { ChallengeId = id };
        return new AccountResult(purpose, null, id, announced, string.Empty, string.Empty);
    }

    private Pending? Take(string id, string purpose)
    {
        lock (_pending)
        {
            var index = _pending.FindIndex(item => item.Id == id && item.Purpose == purpose && item.Expires > DateTimeOffset.UtcNow);
            if (index < 0)
            {
                return null;
            }

            var pending = _pending[index];
            _pending.RemoveAt(index);
            return pending;
        }
    }

    private async Task<JsonElement?> PostAsync(string path, object body, bool authorized, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        Authorize(request, authorized);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<JsonElement?> PatchAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(body),
        };
        Authorize(request, true);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<JsonElement?> GetAsync(string path, bool authorized, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Authorize(request, authorized);
        return await SendAsync(request, cancellationToken);
    }

    private void Authorize(HttpRequestMessage request, bool authorized)
    {
        if (authorized && _settings.ServiceToken.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _settings.ServiceToken);
        }
    }

    private async Task<JsonElement?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return document;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string Nested(JsonElement? element, params string[] path)
    {
        if (element is null)
        {
            return string.Empty;
        }

        var current = element.Value;
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

    private static string Host(string origin)
    {
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return "localhost";
    }

    private static AccountResult Invalid(string detail)
    {
        return new AccountResult("invalid", null, string.Empty, null, string.Empty, detail);
    }

    private sealed record Pending(string Id, string UserId, string Purpose, string Secret, DateTimeOffset Expires);
}
