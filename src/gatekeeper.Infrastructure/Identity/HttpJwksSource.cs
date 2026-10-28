using System.Security.Cryptography;
using System.Text.Json;
using gatekeeper.Identity;

namespace gatekeeper.Identity.Adapters;

public sealed class HttpJwksSource : ISigningKeySource
{
    private readonly HttpClient _client;
    private readonly Cache _cache;

    public HttpJwksSource(HttpClient client)
    {
        _client = client;
        _cache = new Cache();
    }

    public async Task<RSA?> FindAsync(string? keyId, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        lock (_cache.Keys)
        {
            if (!string.IsNullOrEmpty(keyId) && _cache.Keys.TryGetValue(keyId, out var match))
            {
                return match;
            }

            if (_cache.Keys.Count == 1)
            {
                foreach (var only in _cache.Keys.Values)
                {
                    return only;
                }
            }

            return null;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache.Loaded)
        {
            return;
        }

        string json;
        try
        {
            json = await _client.GetStringAsync(_client.BaseAddress, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return;
        }
        catch (JsonException)
        {
            return;
        }

        var parsed = JwksDocument.Read(json);
        lock (_cache.Keys)
        {
            foreach (var key in parsed)
            {
                _cache.Keys[key.KeyId] = key.Key;
            }

            _cache.Loaded = true;
        }
    }

    private sealed class Cache
    {
        public Dictionary<string, RSA> Keys { get; } = new(StringComparer.Ordinal);

        public bool Loaded { get; set; }
    }
}
