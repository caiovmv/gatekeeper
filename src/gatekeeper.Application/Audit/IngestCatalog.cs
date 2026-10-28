using gatekeeper.Authorization;
using gatekeeper.Common;

namespace gatekeeper.Audit;

public sealed class IngestCatalog
{
    private readonly IAuthorizer _authorizer;
    private readonly IAuditTrail _audit;
    private readonly IAccessLogStore _logs;

    public IngestCatalog(IAuthorizer authorizer, IAuditTrail audit, IAccessLogStore logs)
    {
        _authorizer = authorizer;
        _audit = audit;
        _logs = logs;
    }

    public async Task<Resultado<AccessLog>> LogAsync(Principal principal, AccessLog entry, CancellationToken cancellationToken)
    {
        var path = SemConsulta(entry.Path);
        var saved = entry with
        {
            Correlation = entry.Correlation.Trim(),
            Subject = string.IsNullOrWhiteSpace(entry.Subject) ? principal.Subject : entry.Subject.Trim(),
            Method = entry.Method.Trim().ToUpperInvariant(),
            Path = path,
        };
        var valid = saved.Correlation.Length > 0 && saved.Method.Length > 0 && saved.Path.StartsWith('/') && saved.Status >= 100 && saved.LatencyMs >= 0;
        if (!valid)
        {
            return Resultado<AccessLog>.Falha(Erro.Validacao("access log is invalid"));
        }

        if (!await _authorizer.AllowAsync(new DecisionRequest(principal, "ingest", new Resource("access-log", saved.Correlation, principal.Organization)), cancellationToken))
        {
            return Resultado<AccessLog>.Falha(Erro.NaoAutorizado("denied"));
        }

        _logs.Append(saved);
        return Resultado<AccessLog>.Ok(saved);
    }

    public async Task<Resultado<AuditEntry>> AuditAsync(Principal principal, string actor, string action, string resource, string outcome, string correlation, CancellationToken cancellationToken)
    {
        var savedActor = string.IsNullOrWhiteSpace(actor) ? AuditActor.From(principal.Subject) : actor.Trim();
        var savedAction = action.Trim();
        var savedResource = resource.Trim();
        var savedOutcome = outcome.Trim();
        var savedCorrelation = correlation.Trim();
        if (savedAction.Length == 0 || savedResource.Length == 0 || savedOutcome.Length == 0 || savedCorrelation.Length == 0)
        {
            return Resultado<AuditEntry>.Falha(Erro.Validacao("audit event is invalid"));
        }

        if (!await _authorizer.AllowAsync(new DecisionRequest(principal, "ingest", new Resource("audit", savedCorrelation, principal.Organization)), cancellationToken))
        {
            return Resultado<AuditEntry>.Falha(Erro.NaoAutorizado("denied"));
        }

        var entry = _audit.PublishEntry(savedActor, savedAction, savedResource, savedOutcome, savedCorrelation);
        return Resultado<AuditEntry>.Ok(entry);
    }

    public Task<Resultado<IReadOnlyList<AccessLog>>> ListLogsAsync(Principal principal, CancellationToken cancellationToken)
    {
        return ReadAsync(principal);
    }

    private async Task<Resultado<IReadOnlyList<AccessLog>>> ReadAsync(Principal principal)
    {
        if (!await _authorizer.AllowAsync(new DecisionRequest(principal, "read", new Resource("access-log", "access-log", principal.Organization)), CancellationToken.None))
        {
            return Resultado<IReadOnlyList<AccessLog>>.Falha(Erro.NaoAutorizado("denied"));
        }

        return Resultado<IReadOnlyList<AccessLog>>.Ok(_logs.Entries);
    }

    private static string SemConsulta(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        var query = trimmed.IndexOf('?');
        if (query >= 0)
        {
            trimmed = trimmed.Substring(0, query);
        }

        var fragment = trimmed.IndexOf('#');
        if (fragment >= 0)
        {
            trimmed = trimmed.Substring(0, fragment);
        }

        return trimmed;
    }
}
