namespace gatekeeper.Files.Adapters;

public sealed class OpcoesDeArquivo
{
    public OpcoesDeArquivo(
        string provedor,
        string? endpoint,
        string regiao,
        string bucket,
        string accessKey,
        string secretKey,
        bool forcarPathStyle)
    {
        Provedor = provedor;
        Endpoint = endpoint;
        Regiao = regiao;
        Bucket = bucket;
        AccessKey = accessKey;
        SecretKey = secretKey;
        ForcarPathStyle = forcarPathStyle;
    }

    public string Provedor { get; }

    public string? Endpoint { get; }

    public string Regiao { get; }

    public string Bucket { get; }

    public string AccessKey { get; }

    public string SecretKey { get; }

    public bool ForcarPathStyle { get; }

    public static OpcoesDeArquivo Montar(
        string provedorBruto,
        string? endpoint,
        string? regiao,
        string? bucket,
        string? accessKey,
        string? secretKey,
        bool? forcarPathStyle)
    {
        var provedor = ProvedoresDeArquivo.Canonico(provedorBruto);
        var destino = DestinoS3.Para(provedor, endpoint, regiao, forcarPathStyle);
        return new OpcoesDeArquivo(
            provedor,
            destino.Endpoint,
            destino.Regiao,
            (bucket ?? string.Empty).Trim(),
            (accessKey ?? string.Empty).Trim(),
            (secretKey ?? string.Empty).Trim(),
            destino.ForcarPathStyle);
    }
}
