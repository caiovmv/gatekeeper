using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gatekeeper.Caching;
using gatekeeper.Health;
using gatekeeper.Health.Adapters;
using StackExchange.Redis;

namespace gatekeeper.Caching.Adapters;

/// <summary>
/// Protocolo RESP. Redis e Valkey usam esta classe. A connection string e que aponta o servidor.
/// </summary>
public sealed class ValkeyCache : ICache, ICacheDeLeitura, ISondaDeInfraestrutura, IDisposable
{
    private readonly Lazy<IConnectionMultiplexer> _conexao;

    private readonly string _prefixo;

    public ValkeyCache(string connectionString)
        : this(connectionString, string.Empty)
    {
    }

    public ValkeyCache(string connectionString, string prefixo)
    {
        _conexao = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(connectionString));
        _prefixo = prefixo ?? string.Empty;
    }

    public async Task<string?> ObterAsync(string chave, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valor = await Banco().StringGetAsync(chave);
        if (valor.IsNullOrEmpty)
        {
            return null;
        }

        return valor.ToString();
    }

    public async Task GravarAsync(string chave, string valor, TimeSpan expiracao, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiracao <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiracao));
        }

        await Banco().StringSetAsync(chave, valor, expiracao);
    }

    public async Task RemoverAsync(string chave, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Banco().KeyDeleteAsync(chave);
    }

    async Task<LeituraGuardada?> ICacheDeLeitura.ObterAsync(string chave, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valor = await Banco().StringGetAsync(ChaveDeLeitura(chave));
        if (valor.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LeituraGuardada>(valor.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task GravarAsync(string chave, LeituraGuardada leitura, IReadOnlyCollection<string> etiquetas, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var banco = Banco();
        var redis = ChaveDeLeitura(chave);
        await banco.StringSetAsync(redis, JsonSerializer.Serialize(leitura), PoliticaDeCache.Validade);
        foreach (var etiqueta in etiquetas.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal))
        {
            var conjunto = ChaveDeEtiqueta(etiqueta);
            await banco.SetAddAsync(conjunto, redis);
            await banco.KeyExpireAsync(conjunto, PoliticaDeCache.Validade);
        }
    }

    public async Task RemoverPorEtiquetasAsync(IReadOnlyCollection<string> etiquetas, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var banco = Banco();
        foreach (var etiqueta in etiquetas.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal))
        {
            var conjunto = ChaveDeEtiqueta(etiqueta);
            var membros = await banco.SetMembersAsync(conjunto);
            if (membros.Length > 0)
            {
                var chaves = new RedisKey[membros.Length];
                for (var indice = 0; indice < membros.Length; indice++)
                {
                    chaves[indice] = membros[indice].ToString();
                }

                await banco.KeyDeleteAsync(chaves);
            }

            await banco.KeyDeleteAsync(conjunto);
        }
    }

    private string ChaveDeLeitura(string chave)
    {
        var material = _prefixo.Length == 0 ? chave : _prefixo + "\n" + chave;
        return "gatekeeper:leitura:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private string ChaveDeEtiqueta(string etiqueta)
    {
        if (_prefixo.Length == 0)
        {
            return "gatekeeper:leitura:etiqueta:" + etiqueta;
        }

        return "gatekeeper:leitura:etiqueta:" + _prefixo + ":" + etiqueta;
    }

    public string Nome => NomesDeSonda.Cache;

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        return Verificacao.Executar(PingAsync, cancellationToken);
    }

    public void Dispose()
    {
        if (_conexao.IsValueCreated)
        {
            _conexao.Value.Dispose();
        }
    }

    private IDatabase Banco()
    {
        return _conexao.Value.GetDatabase();
    }

    private async Task PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Banco().PingAsync();
    }
}
