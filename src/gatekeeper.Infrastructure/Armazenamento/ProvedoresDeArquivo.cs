namespace gatekeeper.Files.Adapters;

/// <summary>
/// AWS e Amazon sao o mesmo S3. Wasabi, SeaweedFS e Google entram pelo protocolo S3, mudando endpoint.
/// Qualquer outro nome e um S3 compativel com endpoint explicito.
/// </summary>
public static class ProvedoresDeArquivo
{
    public const string Aws = "Aws";

    public const string Wasabi = "Wasabi";

    public const string Minio = "Minio";

    public const string Seaweed = "Seaweed";

    public const string Google = "Google";

    public const string Outro = "Outro";

    public static string Canonico(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
        {
            return Wasabi;
        }

        if (Eh(provedor, "Aws") || Eh(provedor, "Amazon") || Eh(provedor, "S3"))
        {
            return Aws;
        }

        if (Eh(provedor, "Wasabi"))
        {
            return Wasabi;
        }

        if (Eh(provedor, "Minio") || Eh(provedor, "MinIO"))
        {
            return Minio;
        }

        if (Eh(provedor, "Seaweed") || Eh(provedor, "SeaweedFS"))
        {
            return Seaweed;
        }

        if (Eh(provedor, "Google") || Eh(provedor, "GCS") || Eh(provedor, "GoogleCloud"))
        {
            return Google;
        }

        return Outro;
    }

    private static bool Eh(string valor, string esperado)
    {
        return string.Equals(valor.Trim(), esperado, StringComparison.OrdinalIgnoreCase);
    }
}
