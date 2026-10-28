namespace gatekeeper.Identity;

public sealed record AccountSession(string Subject, string Organization, IReadOnlyList<string> Roles);

public sealed record AccountProfile(string Email, string DisplayName);

public sealed record PasskeyChallenge(string ChallengeId, string Challenge, string RpId, string UserId, string UserName, IReadOnlyList<string> CredentialIds);

public sealed record AccountResult(string Status, AccountSession? Session, string Challenge, PasskeyChallenge? Passkey, string Hint, string Detail);

public sealed record AccountStatus(bool SetupRequired, string Provider);

public interface IAccountDirectory
{
    AccountStatus Status();

    Task<AccountResult> SetupAsync(string email, string password, string displayName, CancellationToken cancellationToken);

    Task<AccountResult> SignInAsync(string email, string password, CancellationToken cancellationToken);

    Task<AccountResult> ConfirmFactorAsync(string challenge, string code, CancellationToken cancellationToken);

    Task<AccountResult> PasskeyOptionsAsync(string email, string origin, CancellationToken cancellationToken);

    Task<AccountResult> ConfirmPasskeyAsync(string challenge, string credential, string origin, CancellationToken cancellationToken);

    Task<AccountResult> RequestResetAsync(string email, CancellationToken cancellationToken);

    Task<AccountResult> CompleteResetAsync(string email, string code, string password, CancellationToken cancellationToken);

    Task<AccountResult> BeginMfaAsync(string subject, CancellationToken cancellationToken);

    Task<AccountResult> EnableMfaAsync(string subject, string code, CancellationToken cancellationToken);

    Task<AccountResult> BeginPasskeyRegistrationAsync(string subject, string origin, CancellationToken cancellationToken);

    Task<AccountResult> FinishPasskeyRegistrationAsync(string subject, string challenge, string credential, string origin, CancellationToken cancellationToken);

    Task<AccountResult> ProvisionAsync(string email, string displayName, string role, Guid organizationId, string password, CancellationToken cancellationToken);

    Task RetireAsync(string email, CancellationToken cancellationToken);

    AccountSession? Find(string subject);

AccountProfile? Profile(string subject);
}
