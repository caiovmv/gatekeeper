namespace gatekeeper.Health;

public sealed record RelatorioDeSaude(string Status, IReadOnlyDictionary<string, ComponenteDeSaude> Componentes);

public sealed record ComponenteDeSaude(string Status, string Detalhe);
