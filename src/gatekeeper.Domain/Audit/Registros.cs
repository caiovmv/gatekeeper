namespace gatekeeper.Audit;

public sealed record AuditEntry(
    string OccurredAt,
    string Actor,
    string Action,
    string Resource,
    string Outcome,
    string Correlation,
    string PreviousHash,
    string Hash);

public sealed record AccessLog(
    DateTimeOffset OccurredAt,
    string Correlation,
    string Subject,
    Guid OrganizationId,
    string Method,
    string Path,
    int Status,
    long LatencyMs);
