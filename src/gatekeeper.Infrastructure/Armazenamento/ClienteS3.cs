using Amazon;
using Amazon.S3;

namespace gatekeeper.Files.Adapters;

public static class ClienteS3
{
    public static IAmazonS3 Criar(OpcoesDeArquivo opcoes)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = opcoes.ForcarPathStyle,
            AuthenticationRegion = opcoes.Regiao,
        };

        if (string.IsNullOrWhiteSpace(opcoes.Endpoint))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(opcoes.Regiao);
        }
        else
        {
            config.ServiceURL = opcoes.Endpoint;
        }

        return new AmazonS3Client(opcoes.AccessKey, opcoes.SecretKey, config);
    }
}
