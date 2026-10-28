namespace gatekeeper.Api;

public sealed class OpcoesDeAutenticacao
{
    public OpcoesDeAutenticacao(
        string? chave,
        bool jwtHabilitado,
        string? emissor,
        string? audiencia,
        string? chaveJwt,
        string? platformIssuer,
        string? platformAudience,
        string? sessionIssuer,
        string? sessionAudience,
        string? sessionKey)
    {
        Chave = string.IsNullOrWhiteSpace(chave) ? null : chave.Trim();
        JwtHabilitado = jwtHabilitado;
        Emissor = string.IsNullOrWhiteSpace(emissor) ? string.Empty : emissor.Trim();
        Audiencia = string.IsNullOrWhiteSpace(audiencia) ? string.Empty : audiencia.Trim();
        ChaveJwt = string.IsNullOrWhiteSpace(chaveJwt) ? string.Empty : chaveJwt.Trim();
        PlatformIssuer = string.IsNullOrWhiteSpace(platformIssuer) ? string.Empty : platformIssuer.Trim();
        PlatformAudience = string.IsNullOrWhiteSpace(platformAudience) ? string.Empty : platformAudience.Trim();
        SessionIssuer = string.IsNullOrWhiteSpace(sessionIssuer) ? "gatekeeper" : sessionIssuer.Trim();
        SessionAudience = string.IsNullOrWhiteSpace(sessionAudience) ? "gatekeeper-console" : sessionAudience.Trim();
        SessionKey = string.IsNullOrWhiteSpace(sessionKey) ? "gatekeeper-local-session-key" : sessionKey.Trim();
    }

    public string? Chave { get; }

    public bool JwtHabilitado { get; }

    public string Emissor { get; }

    public string Audiencia { get; }

    public string ChaveJwt { get; }

    public string PlatformIssuer { get; }

    public string PlatformAudience { get; }

    public string SessionIssuer { get; }

    public string SessionAudience { get; }

    public string SessionKey { get; }
}
