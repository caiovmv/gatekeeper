using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace gatekeeper.Tests.Architecture;

public class ControladoresTests
{
    [Fact]
    public void Controladores_nao_pedem_dominio_nem_infraestrutura()
    {
        var controladores = typeof(Program).Assembly
            .GetTypes()
            .Where(tipo => tipo.IsSubclassOf(typeof(ControllerBase)) && !tipo.IsAbstract);

        Assert.NotEmpty(controladores);

        foreach (var controlador in controladores)
        {
            foreach (var construtor in controlador.GetConstructors())
            {
                foreach (var parametro in construtor.GetParameters())
                {
                    var nome = parametro.ParameterType.Namespace ?? string.Empty;
                    Assert.False(
                        nome.StartsWith("gatekeeper.Domain", StringComparison.Ordinal),
                        controlador.Name + " depende de " + parametro.ParameterType.Name);
                    Assert.False(
                        nome.StartsWith("gatekeeper.Infrastructure", StringComparison.Ordinal),
                        controlador.Name + " depende de " + parametro.ParameterType.Name);
                }
            }
        }
    }
}
