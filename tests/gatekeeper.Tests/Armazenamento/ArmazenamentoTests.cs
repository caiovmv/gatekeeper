using gatekeeper.Files.Adapters;
using Xunit;

namespace gatekeeper.Tests.Armazenamento;

public class ArmazenamentoTests
{
    [Theory]
    [InlineData("AWS")]
    [InlineData("Amazon")]
    [InlineData("S3")]
    public void Aws_e_amazon_sao_o_mesmo_s3(string provedor)
    {
        Assert.Equal(ProvedoresDeArquivo.Aws, ProvedoresDeArquivo.Canonico(provedor));
        var destino = DestinoS3.Para(ProvedoresDeArquivo.Aws, null, "sa-east-1", null);
        Assert.Null(destino.Endpoint);
        Assert.False(destino.ForcarPathStyle);
        Assert.Equal("sa-east-1", destino.Regiao);
    }

    [Fact]
    public void Wasabi_aponta_para_o_endpoint_regional()
    {
        var destino = DestinoS3.Para(ProvedoresDeArquivo.Wasabi, null, "eu-central-1", null);
        Assert.Equal("https://s3.eu-central-1.wasabisys.com", destino.Endpoint);
        Assert.False(destino.ForcarPathStyle);
    }

    [Fact]
    public void Seaweed_exige_endpoint_e_usa_path_style()
    {
        Assert.Equal(ProvedoresDeArquivo.Seaweed, ProvedoresDeArquivo.Canonico("SeaweedFS"));
        var destino = DestinoS3.Para(ProvedoresDeArquivo.Seaweed, "http://seaweedfs-s3:8333", null, null);
        Assert.Equal("http://seaweedfs-s3:8333", destino.Endpoint);
        Assert.True(destino.ForcarPathStyle);
        Assert.Throws<ArgumentException>(() => DestinoS3.Para(ProvedoresDeArquivo.Seaweed, "  ", null, null));
    }

    [Fact]
    public void Minio_exige_endpoint_e_usa_path_style()
    {
        var destino = DestinoS3.Para(ProvedoresDeArquivo.Minio, "http://localhost:9000", null, null);
        Assert.Equal("http://localhost:9000", destino.Endpoint);
        Assert.True(destino.ForcarPathStyle);
        Assert.Throws<ArgumentException>(() => DestinoS3.Para(ProvedoresDeArquivo.Minio, "  ", null, null));
    }

    [Fact]
    public void Google_usa_a_interoperabilidade_s3()
    {
        var destino = DestinoS3.Para(ProvedoresDeArquivo.Google, null, null, null);
        Assert.Equal("https://storage.googleapis.com", destino.Endpoint);
        Assert.True(destino.ForcarPathStyle);
    }

    [Fact]
    public void Outro_s3_exige_endpoint()
    {
        Assert.Equal(ProvedoresDeArquivo.Outro, ProvedoresDeArquivo.Canonico("Backblaze"));
        var destino = DestinoS3.Para(ProvedoresDeArquivo.Outro, "https://s3.exemplo", "us-east-1", null);
        Assert.Equal("https://s3.exemplo", destino.Endpoint);
        Assert.True(destino.ForcarPathStyle);
    }

    [Fact]
    public void Cliente_s3_aceita_os_provedores_sem_abrir_conexao()
    {
        using var aws = ClienteS3.Criar(Opcoes("Aws", null, false));
        using var wasabi = ClienteS3.Criar(Opcoes("Wasabi", "https://s3.us-east-1.wasabisys.com", false));
        using var seaweed = ClienteS3.Criar(Opcoes("Seaweed", "http://seaweedfs-s3:8333", true));
        using var minio = ClienteS3.Criar(Opcoes("Minio", "http://localhost:9000", true));
        using var google = ClienteS3.Criar(Opcoes("Google", "https://storage.googleapis.com", true));
        Assert.NotNull(aws);
        Assert.NotNull(wasabi);
        Assert.NotNull(seaweed);
        Assert.NotNull(minio);
        Assert.NotNull(google);
    }

    [Fact]
    public async Task Memoria_grava_abre_e_remove()
    {
        var armazenamento = new InMemoryFileStore();
        await armazenamento.GravarAsync("voz/a.txt", new MemoryStream("ola"u8.ToArray()), "text/plain", CancellationToken.None);

        var aberto = await armazenamento.AbrirAsync("voz/a.txt", CancellationToken.None);
        Assert.NotNull(aberto);
        using var leitor = new StreamReader(aberto);
        Assert.Equal("ola", await leitor.ReadToEndAsync());

        await armazenamento.RemoverAsync("voz/a.txt", CancellationToken.None);
        Assert.Null(await armazenamento.AbrirAsync("voz/a.txt", CancellationToken.None));

        await armazenamento.GravarAsync("lake/audit/dt=2026-10-28/a.json", new MemoryStream("{}"u8.ToArray()), "application/json", CancellationToken.None);
        await armazenamento.GravarAsync("lake/access-log/dt=2026-10-28/b.json", new MemoryStream("{}"u8.ToArray()), "application/json", CancellationToken.None);
        var auditoria = await armazenamento.ListarAsync("lake/audit/", CancellationToken.None);
        Assert.Equal(new[] { "lake/audit/dt=2026-10-28/a.json" }, auditoria);
    }

    private static OpcoesDeArquivo Opcoes(string provedor, string? endpoint, bool pathStyle)
    {
        return new OpcoesDeArquivo(provedor, endpoint, "us-east-1", "gatekeeper", "chave", "segredo", pathStyle);
    }
}
