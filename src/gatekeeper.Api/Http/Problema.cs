namespace gatekeeper.Api;

/// <summary>Corpo de erro da borda. Sem pilha e sem detalhe de infraestrutura.</summary>
public sealed record Problema(string Codigo, string Mensagem);
