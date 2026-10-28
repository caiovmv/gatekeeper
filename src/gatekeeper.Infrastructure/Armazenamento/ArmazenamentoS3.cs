using gatekeeper.Files;
using gatekeeper.Health;
using gatekeeper.Health.Adapters;
using Amazon.S3;
using Amazon.S3.Model;

namespace gatekeeper.Files.Adapters;

/// <summary>
/// Um cliente S3 para AWS, Wasabi, SeaweedFS e a interoperabilidade S3 do Google.
/// O que muda e endpoint, regiao e path-style, montados em <see cref="DestinoS3"/>.
/// </summary>
public sealed class S3FileStore : IFileStore, ISondaDeInfraestrutura, IDisposable
{
    private readonly IAmazonS3 _cliente;
    private readonly string _bucket;

    public S3FileStore(IAmazonS3 cliente, string bucket)
    {
        _cliente = cliente;
        _bucket = bucket;
    }

    public async Task GravarAsync(string caminho, Stream conteudo, string tipoDeConteudo, CancellationToken cancellationToken)
    {
        ExigirCaminho(caminho);
        var pedido = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = caminho,
            InputStream = conteudo,
            ContentType = string.IsNullOrWhiteSpace(tipoDeConteudo) ? "application/octet-stream" : tipoDeConteudo,
        };
        await _cliente.PutObjectAsync(pedido, cancellationToken);
    }

    public async Task<Stream?> AbrirAsync(string caminho, CancellationToken cancellationToken)
    {
        ExigirCaminho(caminho);
        try
        {
            var resposta = await _cliente.GetObjectAsync(_bucket, caminho, cancellationToken);
            return resposta.ResponseStream;
        }
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ListarAsync(string prefixo, CancellationToken cancellationToken)
    {
        var chaves = new List<string>();
        var pedido = new ListObjectsV2Request
        {
            BucketName = _bucket,
            Prefix = prefixo ?? string.Empty,
        };
        ListObjectsV2Response resposta;
        do
        {
            resposta = await _cliente.ListObjectsV2Async(pedido, cancellationToken);
            foreach (var objeto in resposta.S3Objects)
            {
                chaves.Add(objeto.Key);
            }

            pedido.ContinuationToken = resposta.NextContinuationToken;
        }
        while (resposta.IsTruncated == true);

        chaves.Sort(StringComparer.Ordinal);
        return chaves;
    }

    public async Task RemoverAsync(string caminho, CancellationToken cancellationToken)
    {
        ExigirCaminho(caminho);
        await _cliente.DeleteObjectAsync(_bucket, caminho, cancellationToken);
    }

    public string Nome => NomesDeSonda.Arquivos;

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        return Verificacao.Executar(token => _cliente.HeadBucketAsync(new HeadBucketRequest { BucketName = _bucket }, token), cancellationToken);
    }

    public void Dispose()
    {
        _cliente.Dispose();
    }

    private static void ExigirCaminho(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            throw new ArgumentException("Caminho do arquivo e obrigatorio.", nameof(caminho));
        }
    }
}
