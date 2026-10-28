namespace gatekeeper.Identity;

/// <summary>
/// Result of a sign-in for the Flowable orchestrator console. Status is one of authenticated, invalid,
/// mfa (the account needs a second factor, which this flow does not support yet), denied (the account exists but
/// cannot be used here: locked, deactivated or without a role) or unavailable (the service or its setup is at fault).
/// Detail is the message shown to the person, already saying what to do next.
/// </summary>
public sealed record FlowableLoginResult(string Status, string AccessToken, int ExpiresIn, string Detail);

/// <summary>Outcome of creating an account. Status is created or invalid.</summary>
public sealed record FlowableSignUpResult(string Status, string Detail);

/// <summary>Outcome of asking for a password reset. DevCode is only filled in local development.</summary>
public sealed record FlowableResetRequest(bool Accepted, string DevCode, string Detail);

/// <summary>
/// Sign-in, password recovery and password definition for the orchestrator console, without sending the person
/// to the identity provider's own pages. The access token is a real identity-provider token for the orchestrator
/// project, so the orchestrator API validates it exactly as it would a token obtained in the browser.
/// </summary>
public interface IFlowableLogin
{
    Task<FlowableLoginResult> SignInAsync(string email, string password, CancellationToken cancellationToken);

    Task<FlowableSignUpResult> SignUpAsync(string email, string password, string displayName, CancellationToken cancellationToken);

    Task<FlowableResetRequest> RequestResetAsync(string email, CancellationToken cancellationToken);

    Task<bool> CompleteResetAsync(string userId, string email, string code, string password, CancellationToken cancellationToken);
}

/// <summary>Used when no identity provider is configured.</summary>
public sealed class FlowableLoginUnavailable : IFlowableLogin
{
    public Task<FlowableLoginResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        return Task.FromResult(new FlowableLoginResult("unavailable", string.Empty, 0, "The identity provider is not configured."));
    }

    public Task<FlowableSignUpResult> SignUpAsync(string email, string password, string displayName, CancellationToken cancellationToken)
    {
        return Task.FromResult(new FlowableSignUpResult("invalid", "The identity provider is not configured."));
    }

    public Task<FlowableResetRequest> RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        return Task.FromResult(new FlowableResetRequest(false, string.Empty, "The identity provider is not configured."));
    }

    public Task<bool> CompleteResetAsync(string userId, string email, string code, string password, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
