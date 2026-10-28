namespace gatekeeper.Api;

/// <summary>
/// Um modulo, um grupo de rotas. O Program nao acumula mapa de endpoint.
/// </summary>
public interface IModuloDeRotas
{
    void Mapear(IEndpointRouteBuilder rotas);
}
