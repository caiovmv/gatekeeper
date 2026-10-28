namespace gatekeeper.Health;

public static class NomesDeSonda
{
    public const string Banco = "banco";

    public const string Arquivos = "arquivos";

    public const string Vetores = "vetores";

    public const string Cache = "cache";

    public const string LlmSystemTwo = "llm-system-two";

    public const string LlmSystemOne = "llm-system-one";

    public const string MensagensInterno = "mensagens-interno";

    public const string MensagensExterno = "mensagens-externo";

    public const string Workflows = "workflows";

    public const string Tronco = "tronco";

    public static IReadOnlyList<string> Todos { get; } = new[]
    {
        Banco,
        Arquivos,
        Cache,
        MensagensInterno,
        MensagensExterno,
        Workflows,
    };
}
