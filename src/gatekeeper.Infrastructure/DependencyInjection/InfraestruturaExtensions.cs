using gatekeeper.Persistence;
using gatekeeper.Messaging;
using gatekeeper.Health;
using gatekeeper.Messaging.Adapters;
using gatekeeper.Health.Adapters;
using gatekeeper.Persistence.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace gatekeeper.Infrastructure;

public static class InfraestruturaExtensions
{
    public static IServiceCollection AddInfraestruturaEmMemoria(this IServiceCollection services)
    {
        services.AddSingleton<ISondaDeInfraestrutura>(new SondaEmMemoria(NomesDeSonda.Banco));
        services.AddSingleton<IOutbox>(sp => new InMemoryOutbox(
            sp.GetRequiredKeyedService<IMessageBroker>(MessageRoles.Interno)));
        return services;
    }

    public static IServiceCollection AddRepositorioSql(this IServiceCollection services, OpcoesDeBanco opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<IFabricaDeConexao, FabricaDeConexao>();
        services.AddSingleton<IDialetoSql>(DialetosSql.Para(opcoes.ProvedorInvariante));
        services.AddSingleton<SessaoSql>();
        services.AddSingleton<SondaDeBanco>();
        services.AddSingleton<ISondaDeInfraestrutura>(sp => sp.GetRequiredService<SondaDeBanco>());
        services.AddSingleton<IOutbox>(sp => new SqlOutbox(
            sp.GetRequiredService<SessaoSql>(),
            sp.GetRequiredKeyedService<IMessageBroker>(MessageRoles.Interno)));
        return services;
    }
}
