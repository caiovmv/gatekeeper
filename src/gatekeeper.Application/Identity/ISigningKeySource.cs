using System.Security.Cryptography;

namespace gatekeeper.Identity;

public interface ISigningKeySource
{
    Task<RSA?> FindAsync(string? keyId, CancellationToken cancellationToken);
}
