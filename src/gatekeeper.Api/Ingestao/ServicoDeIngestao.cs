using gatekeeper.Audit;
using gatekeeper.Billing.Http;
using Grpc.Core;

namespace gatekeeper.Api.Ingestao;

public sealed class ServicoDeIngestao : Ingest.IngestBase
{
    private readonly IngestCatalog _catalog;

    public ServicoDeIngestao(IngestCatalog catalog)
    {
        _catalog = catalog;
    }

    public override async Task<IngestAck> PublishLog(LogEvent request, ServerCallContext context)
    {
        return await GravarLog(request, context);
    }

    public override async Task<IngestAck> PublishLogs(IAsyncStreamReader<LogEvent> requestStream, ServerCallContext context)
    {
        var accepted = 0;
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var ack = await GravarLog(requestStream.Current, context);
            if (!ack.Accepted)
            {
                return ack;
            }

            accepted++;
        }

        return new IngestAck { Accepted = true, Detail = accepted.ToString() };
    }

    public override async Task<IngestAck> PublishAudit(AuditEvent request, ServerCallContext context)
    {
        return await GravarAuditoria(request, context);
    }

    public override async Task<IngestAck> PublishAudits(IAsyncStreamReader<AuditEvent> requestStream, ServerCallContext context)
    {
        var accepted = 0;
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var ack = await GravarAuditoria(requestStream.Current, context);
            if (!ack.Accepted)
            {
                return ack;
            }

            accepted++;
        }

        return new IngestAck { Accepted = true, Detail = accepted.ToString() };
    }

    private async Task<IngestAck> GravarLog(LogEvent request, ServerCallContext context)
    {
        Guid.TryParse(request.OrganizationId, out var organizationId);
        var entry = new AccessLog(DateTimeOffset.UtcNow, request.Correlation, request.Subject, organizationId, request.Method, request.Path, request.Status, request.LatencyMs);
        var saved = await _catalog.LogAsync(RequestPrincipal.Read(context.GetHttpContext()), entry, context.CancellationToken);
        return Ack(saved.Sucesso, saved.Sucesso ? saved.Valor.Correlation : saved.Erro.Mensagem);
    }

    private async Task<IngestAck> GravarAuditoria(AuditEvent request, ServerCallContext context)
    {
        var saved = await _catalog.AuditAsync(RequestPrincipal.Read(context.GetHttpContext()), request.Actor, request.Action, request.Resource, request.Outcome, request.Correlation, context.CancellationToken);
        return Ack(saved.Sucesso, saved.Sucesso ? saved.Valor.Hash : saved.Erro.Mensagem);
    }

    private static IngestAck Ack(bool accepted, string detail)
    {
        return new IngestAck { Accepted = accepted, Detail = detail };
    }
}
