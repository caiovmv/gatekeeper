namespace gatekeeper.Identity;

public sealed class PlatformTokenOptions
{
    public PlatformTokenOptions(string issuer, string audience)
    {
        Issuer = issuer.Trim();
        Audience = string.IsNullOrWhiteSpace(audience) ? string.Empty : audience.Trim();
        // o projeto do ZITADEL costuma emitir mais de um público (a API e o console): vários, separados por vírgula
        Audiences = Audience.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public string Issuer { get; }

    public string Audience { get; }

    public IReadOnlyList<string> Audiences { get; }
}
