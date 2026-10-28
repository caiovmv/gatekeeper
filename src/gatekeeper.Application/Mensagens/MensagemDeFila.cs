namespace gatekeeper.Messaging;

public sealed record QueueMessage(string Assunto, string Corpo, string? Id = null);
