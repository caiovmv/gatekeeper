using gatekeeper.Common;

namespace gatekeeper.Identity;

public interface IMachineCredential
{
    bool Matches(string presented, string expected);

    Resultado<Guid> ValidateTicket(string key, string ticket, DateTimeOffset now, string origin);
}

public sealed class MachineCredential : IMachineCredential
{
    private readonly IAutenticador _authenticator;

    public MachineCredential(IAutenticador authenticator)
    {
        _authenticator = authenticator;
    }

    public bool Matches(string presented, string expected)
    {
        return _authenticator.Confere(presented, expected);
    }

    public Resultado<Guid> ValidateTicket(string key, string ticket, DateTimeOffset now, string origin)
    {
        return FichaDeBrowser.Validar(key, ticket, now, origin);
    }
}
