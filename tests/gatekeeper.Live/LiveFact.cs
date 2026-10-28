using Xunit;

namespace gatekeeper.Live;

public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GATEKEEPER_LIVE_URL")))
        {
            Skip = "Set GATEKEEPER_LIVE_URL to run this test against the deployed cluster.";
        }
    }
}
