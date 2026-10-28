using System.Collections.Concurrent;
using gatekeeper.Files;
using gatekeeper.Health;

namespace gatekeeper.Files.Adapters;

/// <summary>Usado quando bucket ou credencial vem vazio. O processo sobe sem object storage.</summary>
public sealed class InMemoryFileStore : IFileStore, ISondaDeInfraestrutura
{
    private readonly ConcurrentDictionary<string, byte[]> _arquivos = new();

    public async Task GravarAsync(string caminho, Stream conteudo, string tipoDeConteudo, CancellationToken cancellationToken)
    {
        ExigirCaminho(caminho);
        using var memoria = new MemoryStream();
        await conteudo.CopyToAsync(memoria, cancellationToken);
        _arquivos[caminho] = memoria.ToArray();
    }

    public Task<Stream?> AbrirAsync(string caminho, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirCaminho(caminho);
        if (!_arquivos.TryGetValue(caminho, out var bytes))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
    }

    public Task<IReadOnlyList<string>> ListarAsync(string prefixo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raiz = prefixo ?? string.Empty;
        var chaves = _arquivos.Keys
            .Where(chave => chave.StartsWith(raiz, StringComparison.Ordinal))
            .OrderBy(chave => chave, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(chaves);
    }

    public Task RemoverAsync(string caminho, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExigirCaminho(caminho);
        _arquivos.TryRemove(caminho, out _);
        return Task.CompletedTask;
    }

    public string Nome => NomesDeSonda.Arquivos;

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EstadoDaSonda(true, "memoria"));
    }

    private static void ExigirCaminho(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            throw new ArgumentException("Caminho do arquivo e obrigatorio.", nameof(caminho));
        }
    }
}
