using gatekeeper.Files;
using gatekeeper.Health;
using gatekeeper.Files.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace gatekeeper.Infrastructure;

public static class ArmazenamentoExtensions
{
    public static IServiceCollection AddInMemoryFileStore(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryFileStore>();
        services.AddSingleton<IFileStore>(sp => sp.GetRequiredService<InMemoryFileStore>());
        services.AddSingleton<ISondaDeInfraestrutura>(sp => sp.GetRequiredService<InMemoryFileStore>());
        return services;
    }

    public static IServiceCollection AddS3FileStore(this IServiceCollection services, OpcoesDeArquivo opcoes)
    {
        services.AddSingleton(_ => new S3FileStore(ClienteS3.Criar(opcoes), opcoes.Bucket));
        services.AddSingleton<IFileStore>(sp => sp.GetRequiredService<S3FileStore>());
        services.AddSingleton<ISondaDeInfraestrutura>(sp => sp.GetRequiredService<S3FileStore>());
        return services;
    }
}
