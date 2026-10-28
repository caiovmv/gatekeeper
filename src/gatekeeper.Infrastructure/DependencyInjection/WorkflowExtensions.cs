using gatekeeper.Health;
using gatekeeper.Workflows;
using gatekeeper.Workflows.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace gatekeeper.Infrastructure;

public static class WorkflowExtensions
{
    public static IServiceCollection AddWorkflowEmMemoria(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryWorkflowEngine>();
        services.AddSingleton<IWorkflowEngine>(sp => sp.GetRequiredService<InMemoryWorkflowEngine>());
        services.AddSingleton<ISondaDeInfraestrutura>(sp => sp.GetRequiredService<InMemoryWorkflowEngine>());
        return services;
    }

    public static IServiceCollection AddWorkflowTemporal(this IServiceCollection services, OpcoesDeWorkflow opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<IWorkflowEngine>(_ => new TemporalWorkflowEngine(opcoes));
        services.AddSingleton<ISondaDeInfraestrutura>(sp => (ISondaDeInfraestrutura)sp.GetRequiredService<IWorkflowEngine>());
        services.AddHostedService<TrabalhadorTemporal>();
        return services;
    }
}
