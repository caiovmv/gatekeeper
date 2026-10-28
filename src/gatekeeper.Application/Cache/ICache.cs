namespace gatekeeper.Caching;

/// <summary>
/// Key and value with a deadline. Redis and Valkey speak the same protocol, so the port is one.
/// </summary>
public interface ICache
{
    Task<string?> ObterAsync(string chave, CancellationToken cancellationToken);

    Task GravarAsync(string chave, string valor, TimeSpan expiracao, CancellationToken cancellationToken);

    Task RemoverAsync(string chave, CancellationToken cancellationToken);
}
