namespace gatekeeper.Messaging.Adapters;

public sealed class OpcoesDeMensagens
{
    public OpcoesDeMensagens(string nome, string endpoint, string? token, string stream, string subject)
    {
        Nome = nome;
        Endpoint = endpoint;
        Token = token;
        Stream = stream;
        Subject = subject;
    }

    public string Nome { get; }

    public string Endpoint { get; }

    public string? Token { get; }

    public string Stream { get; }

    public string Subject { get; }
}
