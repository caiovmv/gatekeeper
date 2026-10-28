using System.Globalization;
using System.Text.Json;
using gatekeeper.Files;
using gatekeeper.Identity;

namespace gatekeeper.Audit.Adapters;

/// <summary>
/// Um JSON por evento. O prefixo e de data lake: lake/audit/dt=yyyy-MM-dd e lake/access-log/dt=yyyy-MM-dd.
/// Com credencial e bucket, o arquivo cai no Wasabi. Sem eles, o mesmo JSON fica no armazenamento do processo.
/// </summary>
public sealed class TrilhaNoLago : IAuditTrail
{
    public const string Prefixo = "lake/audit/";
    private const string Cabeca = Prefixo + "head.json";

    private static readonly JsonSerializerOptions Opcoes = CriarOpcoes();
    private readonly IFileStore _arquivos;
    private readonly IAuditPublisher _publicador;
    private readonly object _porta = new();

    public TrilhaNoLago(IFileStore arquivos, IAuditPublisher publicador)
    {
        _arquivos = arquivos;
        _publicador = publicador;
    }

    public IReadOnlyList<AuditEntry> Entries => Ler();

    public AuditEntry PublishEntry(string actor, string action, string resource, string outcome, string correlation)
    {
        AuditEntry entrada;
        lock (_porta)
        {
            var cabeca = LerCabeca();
            entrada = InMemoryAuditTrail.Compose(actor, action, resource, outcome, correlation, cabeca.Hash);
            var quando = DateTimeOffset.Parse(entrada.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var seq = cabeca.Seq + 1;
            var caminho = Prefixo + "dt=" + quando.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" + seq.ToString("D12", CultureInfo.InvariantCulture) + "-" + entrada.Hash + ".json";
            Gravar(caminho, entrada);
            Gravar(Cabeca, new CabecaDaTrilha(entrada.Hash, seq));
        }

        _publicador.Publish(entrada);
        return entrada;
    }

    public bool Verify()
    {
        lock (_porta)
        {
            return InMemoryAuditTrail.Intact(Ler());
        }
    }

    public void Tamper(int index, string outcome)
    {
        lock (_porta)
        {
            var chaves = ChavesDeEvento();
            if (index < 0 || index >= chaves.Count)
            {
                return;
            }

            var atual = Abrir<AuditEntry>(chaves[index]);
            if (atual is null)
            {
                return;
            }

            Gravar(chaves[index], atual with { Outcome = outcome });
        }
    }

    private List<AuditEntry> Ler()
    {
        var entradas = new List<AuditEntry>();
        foreach (var chave in ChavesDeEvento())
        {
            var entrada = Abrir<AuditEntry>(chave);
            if (entrada is not null)
            {
                entradas.Add(entrada);
            }
        }

        return entradas;
    }

    private List<string> ChavesDeEvento()
    {
        var chaves = _arquivos.ListarAsync(Prefixo, CancellationToken.None).GetAwaiter().GetResult()
            .Where(chave => !string.Equals(chave, Cabeca, StringComparison.Ordinal) && chave.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(Seq)
            .ToList();
        return chaves;
    }

    private CabecaDaTrilha LerCabeca()
    {
        return Abrir<CabecaDaTrilha>(Cabeca) ?? new CabecaDaTrilha(string.Empty, 0);
    }

    private T? Abrir<T>(string caminho)
    {
        using var corpo = _arquivos.AbrirAsync(caminho, CancellationToken.None).GetAwaiter().GetResult();
        if (corpo is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(corpo, Opcoes);
    }

    private void Gravar<T>(string caminho, T documento)
    {
        using var corpo = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(documento, Opcoes));
        _arquivos.GravarAsync(caminho, corpo, "application/json", CancellationToken.None).GetAwaiter().GetResult();
    }

    private static long Seq(string chave)
    {
        var nome = chave[(chave.LastIndexOf('/') + 1)..];
        var traco = nome.IndexOf('-');
        if (traco <= 0 || !long.TryParse(nome[..traco], NumberStyles.None, CultureInfo.InvariantCulture, out var seq))
        {
            return long.MaxValue;
        }

        return seq;
    }

    private static JsonSerializerOptions CriarOpcoes()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    private sealed record CabecaDaTrilha(string Hash, long Seq);
}

public sealed class LogsNoLago : IAccessLogStore
{
    public const string Prefixo = "lake/access-log/";

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IFileStore _arquivos;
    private readonly IAccessLogPublisher _publicador;
    private readonly object _porta = new();

    public LogsNoLago(IFileStore arquivos, IAccessLogPublisher publicador)
    {
        _arquivos = arquivos;
        _publicador = publicador;
    }

    public IReadOnlyList<AccessLog> Entries
    {
        get
        {
            lock (_porta)
            {
                return Ler();
            }
        }
    }

    public void Append(AccessLog entry)
    {
        var limpo = entry with
        {
            Correlation = RedacaoDeSegredo.Aplicar(entry.Correlation),
            Subject = RedacaoDeSegredo.Aplicar(entry.Subject),
            Method = (entry.Method ?? string.Empty).Trim().ToUpperInvariant(),
            Path = SemConsulta(RedacaoDeSegredo.Aplicar(entry.Path)),
        };
        var quando = limpo.OccurredAt.ToUniversalTime();
        var caminho = Prefixo
            + "dt=" + quando.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + "/" + quando.UtcTicks.ToString("D20", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")
            + ".json";
        lock (_porta)
        {
            using var corpo = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(limpo, Opcoes));
            _arquivos.GravarAsync(caminho, corpo, "application/json", CancellationToken.None).GetAwaiter().GetResult();
        }

        _publicador.Publish(limpo);
    }

    private List<AccessLog> Ler()
    {
        var chaves = _arquivos.ListarAsync(Prefixo, CancellationToken.None).GetAwaiter().GetResult()
            .Where(chave => chave.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(chave => chave, StringComparer.Ordinal)
            .ToList();
        var entradas = new List<AccessLog>();
        foreach (var chave in chaves)
        {
            using var corpo = _arquivos.AbrirAsync(chave, CancellationToken.None).GetAwaiter().GetResult();
            if (corpo is null)
            {
                continue;
            }

            var entrada = JsonSerializer.Deserialize<AccessLog>(corpo, Opcoes);
            if (entrada is not null)
            {
                entradas.Add(entrada);
            }
        }

        return entradas;
    }

    private static string SemConsulta(string path)
    {
        var texto = (path ?? string.Empty).Trim();
        var consulta = texto.IndexOf('?');
        if (consulta >= 0)
        {
            texto = texto[..consulta];
        }

        var fragmento = texto.IndexOf('#');
        if (fragmento >= 0)
        {
            texto = texto[..fragmento];
        }

        return texto;
    }
}
