using gatekeeper.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace gatekeeper.Application;

public static class AplicacaoExtensions
{
    /// <summary>
    /// Registra o servico concreto e expoe so a porta, ja com o log em volta.
    /// Quem chama resolve a porta, nao a classe concreta.
    /// </summary>
    public static IServiceCollection AddServico<TPedido, TResposta, TServico>(this IServiceCollection services)
        where TServico : class, IApplicationService<TPedido, TResposta>
    {
        services.AddSingleton<TServico>();
        services.AddSingleton<IBehavior<TPedido, TResposta>, LoggingBehavior<TPedido, TResposta>>();
        services.AddSingleton<IApplicationService<TPedido, TResposta>>(provider =>
        {
            var servico = provider.GetRequiredService<TServico>();
            var comportamentos = provider.GetServices<IBehavior<TPedido, TResposta>>().ToList();
            return new ServiceChain<TPedido, TResposta>(servico, comportamentos);
        });

        return services;
    }
}
