using System.Globalization;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace gatekeeper.Api;

/// <summary>
/// Comprime JSON, HTML e os assets do contrato. Brotli vence empate de qualidade; gzip fica de reserva.
/// </summary>
public static class Compressao
{
    public static readonly string[] TiposDeMidia =
    [
        "application/json",
        "application/problem+json",
        "text/html",
        "text/css",
        "text/javascript",
        "application/javascript",
        "image/svg+xml",
    ];

    public static IServiceCollection AddCompressao(this IServiceCollection services)
    {
        services.AddResponseCompression(opcoes =>
        {
            opcoes.EnableForHttps = true;
            opcoes.Providers.Clear();
            opcoes.Providers.Add<BrotliCompressionProvider>();
            opcoes.Providers.Add<GzipCompressionProvider>();
            opcoes.MimeTypes = TiposDeMidia;
        });
        services.Configure<BrotliCompressionProviderOptions>(opcoes =>
        {
            opcoes.Level = CompressionLevel.Fastest;
        });
        services.Configure<GzipCompressionProviderOptions>(opcoes =>
        {
            opcoes.Level = CompressionLevel.Fastest;
        });
        return services;
    }

    public static WebApplication UseCompressao(this WebApplication app)
    {
        app.UseMiddleware<PreferenciaDeCompressao>();
        app.UseResponseCompression();
        return app;
    }
}

public sealed class PreferenciaDeCompressao
{
    private readonly RequestDelegate _proximo;

    public PreferenciaDeCompressao(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public Task InvokeAsync(HttpContext contexto)
    {
        var escolhido = Escolher(contexto.Request.Headers.AcceptEncoding.ToString());
        if (escolhido is not null)
        {
            contexto.Request.Headers.AcceptEncoding = escolhido;
        }

        return _proximo(contexto);
    }

    private static string? Escolher(string aceitos)
    {
        if (string.IsNullOrWhiteSpace(aceitos))
        {
            return null;
        }

        var brotli = -1d;
        var gzip = -1d;
        foreach (var parte in aceitos.Split(','))
        {
            var nome = parte.Trim();
            var qualidade = 1d;
            var separador = nome.IndexOf(';');
            if (separador >= 0)
            {
                var parametro = nome[(separador + 1)..].Trim();
                nome = nome[..separador].Trim();
                if (parametro.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parametro[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var lida))
                {
                    qualidade = lida;
                }
            }

            if (qualidade <= 0 || nome.Length == 0)
            {
                continue;
            }

            if (nome.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                brotli = qualidade;
            }
            else if (nome.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                gzip = qualidade;
            }
        }

        if (brotli < 0 && gzip < 0)
        {
            return null;
        }

        if (brotli >= gzip)
        {
            return "br";
        }

        return "gzip";
    }
}
