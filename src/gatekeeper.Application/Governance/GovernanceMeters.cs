using System.Diagnostics.Metrics;
using gatekeeper.Observability;

namespace gatekeeper.Governance;

public static class GovernanceMeters
{
    private static readonly Meter Meter = new(TelemetryCatalog.Medidor);

    private static int _lockdown;

    private static double _outboxPending;

    private static double _outboxOldest;

    private static double _unattributedAge;

    static GovernanceMeters()
    {
        Meter.CreateObservableGauge("recovery_lockdown_state", ObserveLockdown);
        Meter.CreateObservableGauge("audit_checkpoint_lag_seconds", ObserveLag);
        Meter.CreateObservableGauge("outbox_pending_count", () => new Measurement<double>(_outboxPending));
        Meter.CreateObservableGauge("outbox_oldest_age_seconds", () => new Measurement<double>(_outboxOldest));
        Meter.CreateObservableGauge("unattributed_call_age_seconds", () => new Measurement<double>(_unattributedAge));
    }

    public static readonly Counter<long> AuthorizationDenied = Meter.CreateCounter<long>("authorization_denied_total");

    public static readonly Counter<long> BillingDedup = Meter.CreateCounter<long>("billing_dedup_dropped_total");

    public static readonly Counter<long> ReconciliationRequired = Meter.CreateCounter<long>("external_operation_reconciliation_required_total");

    public static readonly Counter<long> AuditVerificationFailures = Meter.CreateCounter<long>("audit_verification_failures_total");

    public static readonly Counter<long> OutboxPublishFailures = Meter.CreateCounter<long>("outbox_publish_failures_total");

    public static readonly Histogram<double> AuthorizationLatency = Meter.CreateHistogram<double>("authorization_decision_latency");

    public static readonly Histogram<double> RevocationLatency = Meter.CreateHistogram<double>("revocation_effective_latency");

    public static void SetLockdown(bool active)
    {
        _lockdown = active ? 1 : 0;
    }

    public static void SetCheckpointLag(TimeSpan lag)
    {
        CheckpointLagSeconds = Math.Max(0, lag.TotalSeconds);
    }

    public static void SetOutbox(int pending, double oldestSeconds)
    {
        _outboxPending = pending;
        _outboxOldest = Math.Max(0, oldestSeconds);
    }

    public static void SetUnattributedAge(double seconds)
    {
        _unattributedAge = Math.Max(0, seconds);
    }

    public static double CheckpointLagSeconds { get; private set; }

    private static Measurement<int> ObserveLockdown()
    {
        return new Measurement<int>(_lockdown);
    }

    private static Measurement<double> ObserveLag()
    {
        return new Measurement<double>(CheckpointLagSeconds);
    }
}
