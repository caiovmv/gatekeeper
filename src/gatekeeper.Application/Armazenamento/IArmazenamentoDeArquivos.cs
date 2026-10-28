namespace gatekeeper.Files;

/// <summary>
/// Object by path. The application does not know whether the destination is AWS, Wasabi, SeaweedFS or Google.
/// </summary>
public interface IFileStore
{
    Task GravarAsync(string caminho, Stream conteudo, string tipoDeConteudo, CancellationToken cancellationToken);

    Task<Stream?> AbrirAsync(string caminho, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListarAsync(string prefixo, CancellationToken cancellationToken);

    Task RemoverAsync(string caminho, CancellationToken cancellationToken);
}
