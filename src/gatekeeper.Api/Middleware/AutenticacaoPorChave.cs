using gatekeeper.Audit;
using gatekeeper.Billing;
using gatekeeper.Governance;
using gatekeeper.Identity;
using gatekeeper.Observability;
using gatekeeper.Common;

namespace gatekeeper.Api;

/// <summary>
/// Chave vazia deixa a borda aberta, para o esqueleto e os testes. Com chave, exige o cabecalho.
/// JWT ligado exige Bearer valido e nao cai para a chave.
/// </summary>
public sealed class AutenticacaoPorChave
{
    public const string Cabecalho = "X-Api-Key";

    private readonly RequestDelegate _proximo;
    private readonly OpcoesDeAutenticacao _opcoes;

    public AutenticacaoPorChave(RequestDelegate proximo, OpcoesDeAutenticacao opcoes)
    {
        _proximo = proximo;
        _opcoes = opcoes;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (EhSaude(contexto.Request.Path) || EhContaPublica(contexto.Request.Path) || contexto.Request.Path.StartsWithSegments("/docs"))
        {
            await _proximo(contexto);
            return;
        }

        var apresentado = string.Empty;
        var temToken = TemBearer(contexto, out apresentado);
        if (!temToken && contexto.Request.Cookies.TryGetValue("gatekeeper_session", out var cookie) && !string.IsNullOrWhiteSpace(cookie))
        {
            apresentado = cookie;
            temToken = true;
        }

        if (temToken)
        {
            if (Hs256(apresentado) && SessaoAceita(contexto, apresentado))
            {
                await _proximo(contexto);
                return;
            }

            if (_opcoes.PlatformIssuer.Length > 0 && !Hs256(apresentado))
            {
                if (await PlataformaAceita(contexto, apresentado))
                {
                    await _proximo(contexto);
                    return;
                }

                Recusar(contexto, apresentado);
                return;
            }

            if (_opcoes.JwtHabilitado)
            {
                if (ValidadorDeJwt.Validar(apresentado, _opcoes.Emissor, _opcoes.Audiencia, _opcoes.ChaveJwt, DateTimeOffset.UtcNow).Sucesso)
                {
                    await _proximo(contexto);
                    return;
                }

                Recusar(contexto, apresentado);
                return;
            }

            if (Hs256(apresentado))
            {
                Recusar(contexto, apresentado);
                return;
            }
        }

        var chave = _opcoes.Chave;
        var credencial = contexto.RequestServices.GetRequiredService<IMachineCredential>();
        var recebida = contexto.Request.Headers[Cabecalho].ToString();
        var emitida = contexto.RequestServices.GetRequiredService<IMachineKeyGate>().Touch(recebida, DateTimeOffset.UtcNow);
        if (chave is null)
        {
            await _proximo(contexto);
            return;
        }

        if (emitida || credencial.Matches(recebida, chave) || FichaValida(contexto, chave, credencial))
        {
            await _proximo(contexto);
            return;
        }

        Recusar(contexto, recebida);
    }

    private static void Recusar(HttpContext contexto, string recebida)
    {
        contexto.RequestServices.GetRequiredService<ITelemetry>().Rejeicao(CodigosDeErro.NaoAutorizado);
        contexto.RequestServices.GetRequiredService<IAuditTrail>().PublishEntry(
            AuditActor.From(recebida),
            "auth.recusada",
            contexto.Request.Path.Value ?? string.Empty,
            "recusada",
            contexto.TraceIdentifier);
        contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static bool TemBearer(HttpContext contexto, out string token)
    {
        token = string.Empty;
        var cabecalho = contexto.Request.Headers.Authorization.ToString();
        const string prefixo = "Bearer ";
        if (!cabecalho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = cabecalho.Substring(prefixo.Length).Trim();
        return true;
    }

    public const string PrincipalItem = "gatekeeper.Identity.Principal";

    private static async Task<bool> PlataformaAceita(HttpContext contexto, string token)
    {
        var validador = contexto.RequestServices.GetRequiredService<IPlatformTokenValidator>();
        var principal = await validador.ValidateAsync(token, DateTimeOffset.UtcNow, contexto.RequestAborted);
        if (!principal.Sucesso)
        {
            return false;
        }

        var diretorio = contexto.RequestServices.GetRequiredService<IOrganizationDirectory>();
        var esperado = diretorio.IssuerForHost(contexto.Request.Host.Host);
        if (esperado is not null && !string.Equals(esperado, principal.Valor.Issuer, StringComparison.Ordinal))
        {
            return false;
        }

        contexto.Items[PrincipalItem] = principal.Valor;
        return true;
    }

    private static bool FichaValida(HttpContext contexto, string chave, IMachineCredential credencial)
    {
        if (!contexto.Request.Path.StartsWithSegments("/ws"))
        {
            return false;
        }

        var origem = contexto.Request.Headers.Origin.ToString();
        var ficha = contexto.Request.Query["ficha"].ToString();
        return credencial.ValidateTicket(chave, ficha, DateTimeOffset.UtcNow, origem).Sucesso;
    }

    private static bool SessaoAceita(HttpContext contexto, string token)
    {
        var opcoes = contexto.RequestServices.GetRequiredService<OpcoesDeAutenticacao>();
        var sessao = ValidadorDeJwt.LerSessao(token, opcoes.SessionIssuer, opcoes.SessionAudience, opcoes.SessionKey, DateTimeOffset.UtcNow);
        if (!sessao.Sucesso)
        {
            return false;
        }

        var clock = contexto.RequestServices.GetRequiredService<SessionClock>();
        if (clock.TokenRevoked(token) || clock.Expired(sessao.Valor.Subject, DateTimeOffset.UtcNow))
        {
            return false;
        }

        contexto.Items[PrincipalItem] = sessao.Valor;
        return true;
    }

    private static bool Hs256(string token)
    {
        var partes = token.Split('.');
        if (partes.Length != 3)
        {
            return false;
        }

        var cabecalho = Base64Url.DecodeText(partes[0]);
        return cabecalho is not null && cabecalho.Contains("\"alg\":\"HS256\"", StringComparison.Ordinal);
    }

    private static bool EhContaPublica(PathString caminho)
    {
        return caminho.StartsWithSegments("/api/account/status")
            || caminho.StartsWithSegments("/api/account/setup")
            || caminho.StartsWithSegments("/api/account/login")
            || caminho.StartsWithSegments("/api/account/mfa")
            || caminho.StartsWithSegments("/api/account/passkey")
            || caminho.StartsWithSegments("/api/account/password-reset")
            || caminho.StartsWithSegments("/api/account/oidc")
            || caminho.StartsWithSegments("/api/account/saml");
    }

    private static bool EhSaude(PathString caminho)
    {
        return caminho.StartsWithSegments("/health");
    }
}

public static class AutenticacaoPorChaveExtensions
{
    public static WebApplication UseAutenticacaoPorChave(this WebApplication app)
    {
        app.UseMiddleware<AutenticacaoPorChave>();
        return app;
    }
}
