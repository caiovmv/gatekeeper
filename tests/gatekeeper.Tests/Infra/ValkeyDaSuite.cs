using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace gatekeeper.Tests.Cache;

internal static class ValkeyDaSuite
{
    internal const string Nome = "gatekeeper-leitura-suite";

    private const string Senha = "valkey-dev";

    private const string Imagem = "valkey/valkey:9.1.2";

    [ModuleInitializer]
    internal static void Subir()
    {
        AppDomain.CurrentDomain.ProcessExit += delegate
        {
            Derrubar();
        };
        var porta = PortaLivre();
        Derrubar();
        var erro = Docker("run -d --name " + Nome + " -p 127.0.0.1:" + porta + ":6379 " + Imagem + " --requirepass " + Senha);
        if (erro.Codigo != 0)
        {
            throw new InvalidOperationException(erro.Texto);
        }

        EsperarPorta(porta);
        EsperarPing();
        Environment.SetEnvironmentVariable("Caching__Provider", "Valkey");
        Environment.SetEnvironmentVariable("Caching__ConnectionString", "127.0.0.1:" + porta + ",password=" + Senha + ",abortConnect=false");
        Environment.SetEnvironmentVariable("Caching__IsolarHospedeiro", "true");
    }

    internal static int Prazo(string prefixo)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < limite)
        {
            var chaves = Docker("exec " + Nome + " valkey-cli --no-auth-warning -a " + Senha + " KEYS gatekeeper:leitura:etiqueta:" + prefixo + ":*");
            var etiqueta = chaves.Texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (etiqueta is not null)
            {
                var prazo = Docker("exec " + Nome + " valkey-cli --no-auth-warning -a " + Senha + " TTL " + etiqueta);
                if (int.TryParse(prazo.Texto.Trim(), out var segundos))
                {
                    return segundos;
                }
            }

            Thread.Sleep(200);
        }

        return -1;
    }

    private static void Derrubar()
    {
        Docker("rm -f " + Nome);
    }

    private static int PortaLivre()
    {
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        var porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        ouvinte.Stop();
        return porta;
    }

    private static void EsperarPorta(int porta)
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite)
        {
            try
            {
                using var soquete = new TcpClient();
                soquete.Connect(IPAddress.Loopback, porta);
                return;
            }
            catch (SocketException)
            {
                Thread.Sleep(200);
            }
        }

        throw new TimeoutException("Valkey nao abriu a porta " + porta + ".");
    }

    private static void EsperarPing()
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite)
        {
            var ping = Docker("exec " + Nome + " valkey-cli --no-auth-warning -a " + Senha + " PING");
            if (ping.Texto.Contains("PONG", StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException("Valkey nao respondeu PING.");
    }

    private static Saida Docker(string argumentos)
    {
        using var processo = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = argumentos,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        if (processo is null)
        {
            return new Saida(1, "docker nao iniciou");
        }

        var saida = processo.StandardOutput.ReadToEnd();
        var erro = processo.StandardError.ReadToEnd();
        processo.WaitForExit();
        var texto = string.IsNullOrWhiteSpace(saida) ? erro : saida;
        return new Saida(processo.ExitCode, texto.Trim());
    }

    private sealed record Saida(int Codigo, string Texto);
}
