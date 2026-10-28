using System.Diagnostics;
using gatekeeper.Governance;

namespace gatekeeper.Authorization;

public sealed class TimedAuthorizer : IAuthorizer
{
    private readonly IAuthorizer _inner;

    public TimedAuthorizer(IAuthorizer inner)
    {
        _inner = inner;
    }

    public async Task<bool> AllowAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await _inner.AllowAsync(request, cancellationToken);
        }
        finally
        {
            GovernanceMeters.AuthorizationLatency.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}
