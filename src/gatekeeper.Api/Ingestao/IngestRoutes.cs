using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using gatekeeper.Api;
using gatekeeper.Audit;
using gatekeeper.Common;

namespace gatekeeper.Billing.Http;

public sealed class IngestRoutes : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/api/ingest/logs", PostLog).WithTags("Ingest");
        rotas.MapPost("/api/ingest/audit", PostAudit).WithTags("Ingest");
        rotas.Map("/ws/ingest/logs", ReceiveLogs);
        rotas.Map("/ws/ingest/audit", ReceiveAudit);
    }

    private static async Task<IResult> PostLog(LogBody body, HttpContext context, IngestCatalog catalog, CancellationToken cancellationToken)
    {
        var entry = new AccessLog(
            DateTimeOffset.UtcNow,
            body.Correlation ?? string.Empty,
            body.Subject ?? string.Empty,
            body.OrganizationId,
            body.Method ?? string.Empty,
            body.Path ?? string.Empty,
            body.Status,
            body.LatencyMs);
        return Respond(await catalog.LogAsync(RequestPrincipal.Read(context), entry, cancellationToken));
    }

    private static async Task<IResult> PostAudit(AuditBody body, HttpContext context, IngestCatalog catalog, CancellationToken cancellationToken)
    {
        return Respond(await catalog.AuditAsync(
            RequestPrincipal.Read(context),
            body.Actor ?? string.Empty,
            body.Action ?? string.Empty,
            body.Resource ?? string.Empty,
            body.Outcome ?? string.Empty,
            body.Correlation ?? string.Empty,
            cancellationToken));
    }

    private static Task ReceiveLogs(HttpContext context, IngestCatalog catalog)
    {
        return Receive(context, async (texto, cancellationToken) =>
        {
            var body = JsonSerializer.Deserialize<LogBody>(texto, JsonSerializerOptions.Web);
            if (body is null)
            {
                return false;
            }

            var entry = new AccessLog(DateTimeOffset.UtcNow, body.Correlation ?? string.Empty, body.Subject ?? string.Empty, body.OrganizationId, body.Method ?? string.Empty, body.Path ?? string.Empty, body.Status, body.LatencyMs);
            var saved = await catalog.LogAsync(RequestPrincipal.Read(context), entry, cancellationToken);
            return saved.Sucesso;
        });
    }

    private static Task ReceiveAudit(HttpContext context, IngestCatalog catalog)
    {
        return Receive(context, async (texto, cancellationToken) =>
        {
            var body = JsonSerializer.Deserialize<AuditBody>(texto, JsonSerializerOptions.Web);
            if (body is null)
            {
                return false;
            }

            var saved = await catalog.AuditAsync(RequestPrincipal.Read(context), body.Actor ?? string.Empty, body.Action ?? string.Empty, body.Resource ?? string.Empty, body.Outcome ?? string.Empty, body.Correlation ?? string.Empty, cancellationToken);
            return saved.Sucesso;
        });
    }

    private static async Task Receive(HttpContext context, Func<string, CancellationToken, Task<bool>> accept)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            var resultado = await socket.ReceiveAsync(buffer, context.RequestAborted);
            if (resultado.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "ok", context.RequestAborted);
                return;
            }

            var texto = Encoding.UTF8.GetString(buffer, 0, resultado.Count);
            var ok = await accept(texto, context.RequestAborted);
            var ack = Encoding.UTF8.GetBytes(ok ? "{\"accepted\":true}" : "{\"accepted\":false}");
            await socket.SendAsync(ack, WebSocketMessageType.Text, true, context.RequestAborted);
        }
    }

    private static IResult Respond<T>(Resultado<T> result)
    {
        if (!result.Sucesso && result.Erro.Codigo == CodigosDeErro.NaoAutorizado)
        {
            return Results.Json(RespostaHttp.ParaProblema(result.Erro), statusCode: StatusCodes.Status403Forbidden);
        }

        return RespostaHttp.De(result);
    }
}

public sealed record LogBody(string Correlation, string Subject, Guid OrganizationId, string Method, string Path, int Status, long LatencyMs);

public sealed record AuditBody(string Actor, string Action, string Resource, string Outcome, string Correlation);
