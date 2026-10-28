using gatekeeper.Common;

namespace gatekeeper.Identity;

public interface IPlatformTokenValidator
{
    Task<Resultado<PlatformPrincipal>> ValidateAsync(string token, DateTimeOffset now, CancellationToken cancellationToken);
}
