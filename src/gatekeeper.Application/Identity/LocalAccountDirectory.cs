using System.Security.Cryptography;
using System.Text;

namespace gatekeeper.Identity;

public sealed class LocalBootstrap
{
    public LocalBootstrap(bool allowed)
    {
        Allowed = allowed;
    }

    public bool Allowed { get; }
}

public sealed class LocalAccountDirectory : IAccountDirectory
{
    private readonly State _state = new();
    private readonly bool _bootstrap;

    public LocalAccountDirectory(LocalBootstrap bootstrap)
    {
        _bootstrap = bootstrap.Allowed;
    }

    public AccountStatus Status()
    {
        lock (_state)
        {
            return new AccountStatus(_state.Users.Count == 0, "local");
        }
    }

    public Task<AccountResult> SetupAsync(string email, string password, string displayName, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            if (!_bootstrap)
            {
                return Task.FromResult(Invalid("setup is closed"));
            }

            if (_state.Users.Count > 0)
            {
                return Task.FromResult(Invalid("setup is closed"));
            }

            if (!Accept(email, password))
            {
                return Task.FromResult(Invalid("account is invalid"));
            }

            var user = Create(email, displayName, "Staff.Admin", string.Empty, password);
            _state.Users.Add(user);
            return Task.FromResult(Authenticated(user));
        }
    }

    public Task<AccountResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindEmail(email);
            if (user is null || user.Removed || !Password(user, password))
            {
                return Task.FromResult(Invalid("credentials were rejected"));
            }

            if (user.Mfa)
            {
                return Task.FromResult(Pending(user, "mfa", user.Totp, null));
            }

            return Task.FromResult(Authenticated(user));
        }
    }

    public Task<AccountResult> ConfirmFactorAsync(string challenge, string code, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var pending = Take(challenge, "mfa");
            var user = pending is null ? null : FindId(pending.UserId);
            if (user is null || !OneTimePassword.Matches(pending!.Secret, code, DateTimeOffset.UtcNow))
            {
                return Task.FromResult(Invalid("the code was rejected"));
            }

            return Task.FromResult(Authenticated(user));
        }
    }

    public Task<AccountResult> PasskeyOptionsAsync(string email, string origin, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindEmail(email);
            if (user is null || user.Removed || user.Keys.Count == 0)
            {
                return Task.FromResult(Invalid("no passkey is registered"));
            }

            var challenge = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
            return Task.FromResult(Pending(user, "passkey", challenge, ChallengeOf(user, challenge, origin, false)));
        }
    }

    public Task<AccountResult> ConfirmPasskeyAsync(string challenge, string credential, string origin, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var pending = Take(challenge, "passkey");
            var user = pending is null ? null : FindId(pending.UserId);
            var key = user?.Keys.FirstOrDefault(item => credential.Contains(item.Id, StringComparison.Ordinal));
            if (user is null || key is null || !PasskeyCeremony.Verify(origin, pending!.Secret, key.PublicKey, credential))
            {
                return Task.FromResult(Invalid("the passkey was rejected"));
            }

            return Task.FromResult(Authenticated(user));
        }
    }

    public Task<AccountResult> RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindEmail(email);
            if (user is null || user.Removed)
            {
                return Task.FromResult(new AccountResult("reset", null, string.Empty, null, string.Empty, "If the account exists, the reset code is ready."));
            }

            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            user.ResetHash = Hash(code, user.Salt);
            user.ResetExpires = DateTimeOffset.UtcNow.AddMinutes(15);
            return Task.FromResult(new AccountResult("reset", null, string.Empty, null, code, "Use this code on the reset screen."));
        }
    }

    public Task<AccountResult> CompleteResetAsync(string email, string code, string password, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindEmail(email);
            if (user is null || user.Removed || password.Trim().Length < 8 || user.ResetExpires < DateTimeOffset.UtcNow || !string.Equals(user.ResetHash, Hash(code.Trim(), user.Salt), StringComparison.Ordinal))
            {
                return Task.FromResult(Invalid("the reset was rejected"));
            }

            user.Hash = Hash(password, user.Salt);
            user.ResetHash = string.Empty;
            return Task.FromResult(Authenticated(user));
        }
    }

    public Task<AccountResult> BeginMfaAsync(string subject, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindSubject(subject);
            if (user is null)
            {
                return Task.FromResult(Invalid("account was not found"));
            }

            var secret = OneTimePassword.Secret();
            var uri = "otpauth://totp/gatekeeper:" + Uri.EscapeDataString(user.Email) + "?secret=" + secret + "&issuer=gatekeeper";
            return Task.FromResult(Pending(user, "enroll-mfa", secret, null) with { Hint = uri });
        }
    }

    public Task<AccountResult> EnableMfaAsync(string subject, string code, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindSubject(subject);
            var pending = user is null ? null : Latest(user.Id, "enroll-mfa");
            if (user is null || pending is null || !OneTimePassword.Matches(pending.Secret, code, DateTimeOffset.UtcNow))
            {
                return Task.FromResult(Invalid("the code was rejected"));
            }

            user.Totp = pending.Secret;
            user.Mfa = true;
            return Task.FromResult(Authenticated(user));
        }
    }

    public Task<AccountResult> BeginPasskeyRegistrationAsync(string subject, string origin, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindSubject(subject);
            if (user is null)
            {
                return Task.FromResult(Invalid("account was not found"));
            }

            var challenge = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
            return Task.FromResult(Pending(user, "enroll-passkey", challenge, ChallengeOf(user, challenge, origin, true)));
        }
    }

    public Task<AccountResult> FinishPasskeyRegistrationAsync(string subject, string challenge, string credential, string origin, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindSubject(subject);
            var pending = Take(challenge, "enroll-passkey");
            if (user is null || pending is null || pending.UserId != user.Id || !PasskeyCeremony.TryRegister(origin, pending.Secret, credential, out var id, out var publicKey))
            {
                return Task.FromResult(Invalid("the passkey was rejected"));
            }

            user.Keys.Add(new StoredKey(id, publicKey));
            return Task.FromResult(Authenticated(user));
        }
    }

    public Task<AccountResult> ProvisionAsync(string email, string displayName, string role, Guid organizationId, string password, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var normalized = email.Trim().ToLowerInvariant();
            var user = FindEmail(normalized);
            if (user is null)
            {
                var initial = password.Trim();
                var generated = string.Empty;
                if (initial.Length < 8)
                {
                    initial = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
                    generated = initial;
                }

                user = Create(normalized, displayName, role, organizationId.ToString(), initial);
                _state.Users.Add(user);
                return Task.FromResult(new AccountResult("authenticated", Session(user), string.Empty, null, generated, generated.Length == 0 ? string.Empty : "Share this initial password."));
            }

            user.DisplayName = displayName.Trim();
            user.Role = role;
            user.Organization = organizationId.ToString();
            user.Removed = false;
            if (password.Trim().Length >= 8)
            {
                user.Hash = Hash(password, user.Salt);
            }

            return Task.FromResult(Authenticated(user));
        }
    }

    public Task RetireAsync(string email, CancellationToken cancellationToken)
    {
        lock (_state)
        {
            var user = FindEmail(email);
            if (user is not null)
            {
                user.Removed = true;
            }
        }

        return Task.CompletedTask;
    }

    public AccountSession? Find(string subject)
    {
        lock (_state)
        {
            var user = FindSubject(subject);
            if (user is null || user.Removed)
            {
                return null;
            }

            return Session(user);
        }
    }

    public AccountProfile? Profile(string subject)
    {
        lock (_state)
        {
            var user = FindSubject(subject);
            if (user is null)
            {
                return null;
            }

            return new AccountProfile(user.Email, user.DisplayName);
        }
    }

    private AccountResult Pending(StoredUser user, string purpose, string secret, PasskeyChallenge? passkey)
    {
        var id = Guid.NewGuid().ToString("N");
        _state.Challenges.Add(new StoredChallenge(id, user.Id, purpose, secret, DateTimeOffset.UtcNow.AddMinutes(5)));
        var status = "enroll";
        if (purpose == "mfa")
        {
            status = "mfa";
        }
        else if (purpose == "passkey")
        {
            status = "passkey";
        }

        return new AccountResult(status, null, id, passkey, string.Empty, string.Empty);
    }

    private PasskeyChallenge ChallengeOf(StoredUser user, string challenge, string origin, bool registration)
    {
        var host = "localhost";
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }

        var ids = registration ? Array.Empty<string>() : user.Keys.Select(item => item.Id).ToArray();
        return new PasskeyChallenge(string.Empty, challenge, host, Base64Url.Encode(user.Id.ToByteArray()), user.Email, ids);
    }

    private StoredChallenge? Take(string id, string purpose)
    {
        var index = _state.Challenges.FindIndex(item => item.Id == id && item.Purpose == purpose && item.Expires > DateTimeOffset.UtcNow);
        if (index < 0)
        {
            return null;
        }

        var pending = _state.Challenges[index];
        _state.Challenges.RemoveAt(index);
        return pending;
    }

    private StoredChallenge? Latest(Guid userId, string purpose)
    {
        for (var index = _state.Challenges.Count - 1; index >= 0; index--)
        {
            var pending = _state.Challenges[index];
            if (pending.UserId == userId && pending.Purpose == purpose && pending.Expires > DateTimeOffset.UtcNow)
            {
                return pending;
            }
        }

        return null;
    }

    private StoredUser Create(string email, string displayName, string role, string organization, string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return new StoredUser(Guid.NewGuid(), email.Trim().ToLowerInvariant(), displayName.Trim(), organization, role, salt, Hash(password, salt));
    }

    private StoredUser? FindEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return _state.Users.FirstOrDefault(item => item.Email == normalized);
    }

    private StoredUser? FindId(Guid id)
    {
        return _state.Users.FirstOrDefault(item => item.Id == id);
    }

    private StoredUser? FindSubject(string subject)
    {
        return _state.Users.FirstOrDefault(item => item.Id.ToString() == subject && !item.Removed);
    }

    private static bool Accept(string email, string password)
    {
        return email.Contains('@') && password.Trim().Length >= 8;
    }

    private static bool Password(StoredUser user, string password)
    {
        return string.Equals(user.Hash, Hash(password, user.Salt), StringComparison.Ordinal);
    }

    private static string Hash(string value, string salt)
    {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(value, Encoding.UTF8.GetBytes(salt), 10000, HashAlgorithmName.SHA256, 32);
        return Convert.ToHexString(bytes);
    }

    private static AccountResult Authenticated(StoredUser user)
    {
        return new AccountResult("authenticated", Session(user), string.Empty, null, string.Empty, string.Empty);
    }

    private static AccountSession Session(StoredUser user)
    {
        return new AccountSession(user.Id.ToString(), user.Organization, RolesOf(user.Role));
    }

    private static string[] RolesOf(string role)
    {
        if (role == "admin" || role == "Staff.Admin")
        {
            return new[] { "Staff.Admin", "admin" };
        }

        if (role == "support" || role == "Staff.Support")
        {
            return new[] { "Staff.Support", "support" };
        }

        if (role == "auditor" || role == "Staff.Auditor")
        {
            return new[] { "Staff.Auditor", "auditor" };
        }

        return new[] { role };
    }

    private static AccountResult Invalid(string detail)
    {
        return new AccountResult("invalid", null, string.Empty, null, string.Empty, detail);
    }

    private sealed class State
    {
        public List<StoredUser> Users { get; } = new();

        public List<StoredChallenge> Challenges { get; } = new();
    }

    private sealed class StoredUser
    {
        public StoredUser(Guid id, string email, string displayName, string organization, string role, string salt, string hash)
        {
            Id = id;
            Email = email;
            DisplayName = displayName;
            Organization = organization;
            Role = role;
            Salt = salt;
            Hash = hash;
        }

        public Guid Id { get; }

        public string Email { get; }

        public string DisplayName { get; set; }

        public string Organization { get; set; }

        public string Role { get; set; }

        public string Salt { get; }

        public string Hash { get; set; }

        public string Totp { get; set; } = string.Empty;

        public bool Mfa { get; set; }

        public string ResetHash { get; set; } = string.Empty;

        public DateTimeOffset ResetExpires { get; set; }

        public bool Removed { get; set; }

        public List<StoredKey> Keys { get; } = new();
    }

    private sealed record StoredKey(string Id, string PublicKey);

    private sealed record StoredChallenge(string Id, Guid UserId, string Purpose, string Secret, DateTimeOffset Expires);
}
