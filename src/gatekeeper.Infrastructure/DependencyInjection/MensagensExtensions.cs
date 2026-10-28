using gatekeeper.Messaging;
using gatekeeper.Health;
using gatekeeper.Messaging.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace gatekeeper.Infrastructure;

public static class MensagensExtensions
{
    public static IServiceCollection AddMensagensEmMemoria(this IServiceCollection services, string papel, string nome)
    {
        var broker = new InMemoryMessageBroker(nome);
        services.AddKeyedSingleton<IMessageBroker>(papel, broker);
        services.AddSingleton<ISondaDeInfraestrutura>(broker);
        return services;
    }

    public static IServiceCollection AddMensagensJetStream(this IServiceCollection services, string papel, OpcoesDeMensagens opcoes)
    {
        services.AddKeyedSingleton<IMessageBroker>(papel, (_, _) => new JetStreamBroker(opcoes));
        services.AddSingleton<ISondaDeInfraestrutura>(sp => (ISondaDeInfraestrutura)sp.GetRequiredKeyedService<IMessageBroker>(papel));
        return services;
    }
}
