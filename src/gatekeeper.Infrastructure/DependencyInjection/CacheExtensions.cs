using gatekeeper.Caching;
using gatekeeper.Health;
using gatekeeper.Caching.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace gatekeeper.Infrastructure;

public static class CacheExtensions
{
    public static IServiceCollection AddInMemoryCache(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InMemoryCache>();
        services.AddSingleton<ICache>(sp => sp.GetRequiredService<InMemoryCache>());
        services.AddSingleton<ISondaDeInfraestrutura>(sp => sp.GetRequiredService<InMemoryCache>());
        services.AddSingleton<ICacheDeLeitura>(sp => new CacheDeLeituraEmMemoria(sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    public static IServiceCollection AddValkeyCache(this IServiceCollection services, string connectionString, string prefixo)
    {
        services.AddSingleton(_ => new ValkeyCache(connectionString, prefixo));
        services.AddSingleton<ICache>(sp => sp.GetRequiredService<ValkeyCache>());
        services.AddSingleton<ICacheDeLeitura>(sp => sp.GetRequiredService<ValkeyCache>());
        services.AddSingleton<ISondaDeInfraestrutura>(sp => sp.GetRequiredService<ValkeyCache>());
        return services;
    }
}
