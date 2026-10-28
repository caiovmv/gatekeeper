using System.Text.Json.Serialization;

namespace gatekeeper.Messaging.Adapters;

public sealed record EnvelopeDeMensagem(string Assunto, string Corpo);

[JsonSerializable(typeof(EnvelopeDeMensagem))]
internal sealed partial class ContextoDeEnvelope : JsonSerializerContext
{
}
