using System.Security.Cryptography;
using System.Text;
using gatekeeper.Caching;
using gatekeeper.Identity;

namespace gatekeeper.Api;

/// <summary>
/// Guarda GET autenticado de JSON e apaga a familia quando POST, PUT, PATCH ou DELETE concluem.
/// O navegador segue com no-store. O prazo de cinco minutos e o failsafe.
/// </summary>
public sealed class CacheDeLeitura
{
    private readonly RequestDelegate _proximo;

    public CacheDeLeitura(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto, ICacheDeLeitura cache)
    {
        if (Fora(contexto.Request.Path))
        {
            await _proximo(contexto);
            return;
        }

        if (EhMutacao(contexto.Request.Method))
        {
            await ExecutarMutacaoAsync(contexto, cache);
            return;
        }

        if (!HttpMethods.IsGet(contexto.Request.Method))
        {
            await _proximo(contexto);
            return;
        }

        var etiquetas = MapaDeEtiquetas.DeLeitura(contexto.Request.Path);
        var escopo = Escopo(contexto);
        if (escopo is null || etiquetas.Count == 0)
        {
            await _proximo(contexto);
            return;
        }

        var chave = MontarChave(escopo, contexto.Request);
        var guardada = await cache.ObterAsync(chave, contexto.RequestAborted);
        if (guardada is not null)
        {
            contexto.Response.StatusCode = guardada.Status;
            contexto.Response.ContentType = guardada.Tipo;
            contexto.Response.Headers[CabecalhosDeCache.Estado] = CabecalhosDeCache.Acerto;
            contexto.Response.Headers[CabecalhosDeCache.Etiquetas] = guardada.Etiquetas;
            await contexto.Response.WriteAsync(guardada.Corpo, contexto.RequestAborted);
            return;
        }

        var original = contexto.Response.Body;
        await using var buffer = new MemoryStream();
        contexto.Response.Body = buffer;
        try
        {
            await _proximo(contexto);
        }
        finally
        {
            contexto.Response.Body = original;
        }

        if (PodeGuardar(contexto.Response, buffer))
        {
            buffer.Position = 0;
            using var leitor = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var corpo = await leitor.ReadToEndAsync(contexto.RequestAborted);
            var texto = string.Join(',', etiquetas);
            await cache.GravarAsync(
                chave,
                new LeituraGuardada(contexto.Response.StatusCode, contexto.Response.ContentType ?? "application/json", corpo, texto),
                etiquetas,
                contexto.RequestAborted);
            contexto.Response.Headers[CabecalhosDeCache.Estado] = CabecalhosDeCache.Falha;
            contexto.Response.Headers[CabecalhosDeCache.Etiquetas] = texto;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(contexto.Response.Body, contexto.RequestAborted);
    }

    private async Task ExecutarMutacaoAsync(HttpContext contexto, ICacheDeLeitura cache)
    {
        var original = contexto.Response.Body;
        await using var buffer = new MemoryStream();
        contexto.Response.Body = buffer;
        try
        {
            await _proximo(contexto);
        }
        finally
        {
            contexto.Response.Body = original;
        }

        if (contexto.Response.StatusCode is >= 200 and < 300)
        {
            var etiquetas = MapaDeEtiquetas.DeEscrita(contexto.Request.Path);
            if (etiquetas.Count > 0)
            {
                contexto.Response.Headers[CabecalhosDeCache.Despejo] = string.Join(',', etiquetas);
                await cache.RemoverPorEtiquetasAsync(etiquetas, contexto.RequestAborted);
            }
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(contexto.Response.Body, contexto.RequestAborted);
    }

    private static bool PodeGuardar(HttpResponse resposta, MemoryStream buffer)
    {
        if (resposta.StatusCode < 200 || resposta.StatusCode >= 300)
        {
            return false;
        }

        if (buffer.Length == 0 || buffer.Length > PoliticaDeCache.TamanhoMaximo)
        {
            return false;
        }

        var tipo = resposta.ContentType;
        return tipo is not null && tipo.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Fora(PathString caminho)
    {
        return caminho.StartsWithSegments("/health")
            || caminho.StartsWithSegments("/api/account")
            || caminho.StartsWithSegments("/ws")
            || caminho.StartsWithSegments("/grpc")
            || caminho.StartsWithSegments("/swagger")
            || caminho.StartsWithSegments("/redoc")
            || caminho.StartsWithSegments("/openapi")
            || caminho.StartsWithSegments("/asyncapi")
            || caminho.StartsWithSegments("/mcp")
            || caminho.StartsWithSegments("/a2a")
            || caminho.StartsWithSegments("/v1")
            || caminho.StartsWithSegments("/docs");
    }

    private static bool EhMutacao(string metodo)
    {
        return HttpMethods.IsPost(metodo)
            || HttpMethods.IsPut(metodo)
            || HttpMethods.IsPatch(metodo)
            || HttpMethods.IsDelete(metodo);
    }

    private static string? Escopo(HttpContext contexto)
    {
        if (contexto.Items[AutenticacaoPorChave.PrincipalItem] is PlatformPrincipal principal)
        {
            var papeis = string.Join("\n", principal.Roles.OrderBy(papel => papel, StringComparer.Ordinal));
            return "p\n" + principal.Organization + "\n" + principal.Subject + "\n" + papeis;
        }

        var chave = contexto.Request.Headers[AutenticacaoPorChave.Cabecalho].ToString();
        if (!string.IsNullOrWhiteSpace(chave))
        {
            return "k\n" + Resumo(chave.Trim());
        }

        var autorizacao = contexto.Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (autorizacao.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return "t\n" + Resumo(autorizacao.Substring(bearer.Length).Trim());
        }

        if (contexto.Request.Cookies.TryGetValue("gatekeeper_session", out var sessao) && !string.IsNullOrWhiteSpace(sessao))
        {
            return "t\n" + Resumo(sessao);
        }

        return null;
    }

    private static string MontarChave(string escopo, HttpRequest pedido)
    {
        return escopo + "\n" + (pedido.Path.Value ?? "/") + "\n" + Consulta(pedido.Query);
    }

    private static string Consulta(IQueryCollection query)
    {
        if (query.Count == 0)
        {
            return string.Empty;
        }

        var pares = new List<string>(query.Count);
        foreach (var par in query.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var valores = par.Value.ToArray();
            Array.Sort(valores, StringComparer.Ordinal);
            pares.Add(par.Key + "=" + string.Join(",", valores));
        }

        return string.Join("&", pares);
    }

    private static string Resumo(string valor)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
    }
}

public static class CacheDeLeituraExtensions
{
    public static WebApplication UseCacheDeLeitura(this WebApplication app)
    {
        app.UseMiddleware<CacheDeLeitura>();
        return app;
    }
}
