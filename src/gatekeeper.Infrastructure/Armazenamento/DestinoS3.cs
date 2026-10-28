namespace gatekeeper.Files.Adapters;

/// <summary>
/// Endpoint, regiao e path-style de cada provedor S3. O cliente HTTP e o mesmo.
/// </summary>
public sealed record DestinoS3(string? Endpoint, string Regiao, bool ForcarPathStyle)
{
    public static DestinoS3 Para(string provedor, string? endpoint, string? regiao, bool? forcarPathStyle)
    {
        var regiaoFinal = "us-east-1";
        if (!string.IsNullOrWhiteSpace(regiao))
        {
            regiaoFinal = regiao.Trim();
        }

        if (provedor == ProvedoresDeArquivo.Aws)
        {
            var endpointAws = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
            return new DestinoS3(endpointAws, regiaoFinal, forcarPathStyle ?? false);
        }

        if (provedor == ProvedoresDeArquivo.Wasabi)
        {
            var url = "https://s3." + regiaoFinal + ".wasabisys.com";
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                url = endpoint.Trim();
            }

            return new DestinoS3(url, regiaoFinal, forcarPathStyle ?? false);
        }

        if (provedor == ProvedoresDeArquivo.Google)
        {
            var url = "https://storage.googleapis.com";
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                url = endpoint.Trim();
            }

            return new DestinoS3(url, regiaoFinal, forcarPathStyle ?? true);
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint e obrigatorio para " + provedor + ".");
        }

        return new DestinoS3(endpoint.Trim(), regiaoFinal, forcarPathStyle ?? true);
    }
}
