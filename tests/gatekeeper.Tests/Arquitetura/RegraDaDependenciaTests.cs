using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace gatekeeper.Tests.Architecture;

public class RegraDaDependenciaTests
{
    [Fact]
    public void Dominio_referencia_apenas_o_framework()
    {
        var referencias = ReferenciasDe("gatekeeper.Domain");

        foreach (var referencia in referencias)
        {
            Assert.True(
                EhDoFramework(referencia),
                "O Domain passou a referenciar '" + referencia + "'.");
        }
    }

    [Fact]
    public void Aplicacao_nao_conhece_infraestrutura_api_nem_banco()
    {
        var referencias = ReferenciasDe("gatekeeper.Application");

        Assert.DoesNotContain("gatekeeper.Infrastructure", referencias);
        Assert.DoesNotContain("gatekeeper.Api", referencias);
        Assert.DoesNotContain(referencias, nome => nome.Contains("EntityFramework", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Dapper", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("AWSSDK", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("StackExchange", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Google", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Qdrant", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("OpenAI", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Anthropic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencias, nome => nome.Contains("NATS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Temporal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencias, nome => nome.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencias, nome => nome.Contains("SIPSorcery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Grpc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Infraestrutura_nao_conhece_a_api()
    {
        var referencias = ReferenciasDe("gatekeeper.Infrastructure");

        Assert.DoesNotContain("gatekeeper.Api", referencias);
        Assert.DoesNotContain(referencias, nome => nome.Contains("AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("SqlClient", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("MySql", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Sqlite", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Google.Cloud", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Qdrant", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, nome => nome.Contains("OpenAI", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencias, nome => nome.Contains("Anthropic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dominio_generico_nao_referencia_chamada_voz_telefonia_nem_motores()
    {
        var genericos = new[]
        {
            "Identity",
            "Authorization",
            "Billing",
            "Audit",
            "Messaging",
            "Files",
            "Caching",
            "Workflows",
            "Observability",
            "Persistence",
        };
        var produtos = new[]
        {
            "gatekeeper.Call",
            "gatekeeper.Voice",
            "gatekeeper.Telephony",
            "gatekeeper.Engines",
        };
        var raiz = RaizDoRepositorio();
        var arquivos = Directory.GetFiles(Path.Combine(raiz, "src"), "*.cs", SearchOption.AllDirectories);

        foreach (var arquivo in arquivos)
        {
            var texto = File.ReadAllText(arquivo);
            var encontrado = Regex.Match(texto, @"^namespace\s+([^\s;{]+)", RegexOptions.Multiline);
            if (!encontrado.Success)
            {
                continue;
            }

            var espaco = encontrado.Groups[1].Value;
            if (!EhGenerico(espaco, genericos))
            {
                continue;
            }

            foreach (var produto in produtos)
            {
                Assert.False(
                    texto.Contains(produto, StringComparison.Ordinal),
                    "O arquivo " + arquivo + " do dominio " + espaco + " menciona " + produto + ".");
            }
        }
    }

    private static bool EhGenerico(string espaco, string[] genericos)
    {
        foreach (var generico in genericos)
        {
            var prefixo = "gatekeeper." + generico;
            if (espaco == prefixo || espaco.StartsWith(prefixo + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string RaizDoRepositorio()
    {
        var atual = new DirectoryInfo(AppContext.BaseDirectory);
        while (atual is not null)
        {
            if (File.Exists(Path.Combine(atual.FullName, "gatekeeper.slnx")))
            {
                return atual.FullName;
            }

            atual = atual.Parent;
        }

        throw new InvalidOperationException("gatekeeper.slnx nao foi encontrado a partir da pasta de saida.");
    }

    private static IReadOnlyList<string> ReferenciasDe(string nomeDaCamada)
    {
        var camada = Assembly.Load(new AssemblyName(nomeDaCamada));
        var nomes = new List<string>();

        foreach (var referenciada in camada.GetReferencedAssemblies())
        {
            nomes.Add(referenciada.Name ?? string.Empty);
        }

        Assert.NotEmpty(nomes);
        return nomes;
    }

    private static bool EhDoFramework(string nomeDoAssembly)
    {
        if (nomeDoAssembly.StartsWith("System", StringComparison.Ordinal))
        {
            return true;
        }

        if (nomeDoAssembly == "netstandard")
        {
            return true;
        }

        return nomeDoAssembly == "mscorlib";
    }
}
