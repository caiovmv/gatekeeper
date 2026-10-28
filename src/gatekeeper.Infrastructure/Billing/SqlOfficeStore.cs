using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using gatekeeper.Identity;
using gatekeeper.Persistence.Adapters;
using Dapper;

namespace gatekeeper.Billing;

public sealed class SqlOfficeStore : IOfficeStore, IMachineKeyGate
{
    private readonly SessaoSql _sessao;

    public SqlOfficeStore(SessaoSql sessao)
    {
        _sessao = sessao;
    }

    public IReadOnlyList<Member> Members()
    {
        using var conexao = Abrir();
        var linhas = conexao.Query<LinhaDeMembro>(
            "SELECT id AS Id, organization_id AS OrganizationId, email AS Email, display_name AS DisplayName, role AS Role FROM office_member WHERE removed_on = ''");
        var membros = new List<Member>();
        foreach (var linha in linhas)
        {
            membros.Add(new Member(Guid.Parse(linha.Id), Guid.Parse(linha.OrganizationId), linha.Email, linha.DisplayName, linha.Role));
        }

        return membros;
    }

    public void Save(Member member)
    {
        Gravar(
            "INSERT INTO office_member (id, organization_id, email, display_name, role, removed_on) VALUES (@id, @organizationId, @email, @displayName, @role, @removed)",
            "UPDATE office_member SET organization_id = @organizationId, email = @email, display_name = @displayName, role = @role, removed_on = @removed WHERE id = @id",
            new
            {
                id = member.Id.ToString("D"),
                organizationId = member.OrganizationId.ToString("D"),
                email = member.Email,
                displayName = member.DisplayName,
                role = member.Role,
                removed = Texto(member.RemovedOn),
            });
    }

    public void RemoveMember(Guid id)
    {
        using var conexao = Abrir();
        conexao.Execute(
            "UPDATE office_member SET removed_on = @removed WHERE id = @id AND removed_on = ''",
            new { id = id.ToString("D"), removed = Texto(DateTimeOffset.UtcNow) });
    }

    public IReadOnlyList<MachineKey> MachineKeys()
    {
        using var conexao = Abrir();
        var linhas = conexao.Query<LinhaDeChave>(
            "SELECT id AS Id, organization_id AS OrganizationId, name AS Name, prefix AS Prefix, created_on AS CreatedOn, revoked_on AS RevokedOn, last_used_on AS LastUsedOn FROM office_machine_key");
        var chaves = new List<MachineKey>();
        foreach (var linha in linhas)
        {
            chaves.Add(new MachineKey(
                Guid.Parse(linha.Id),
                Guid.Parse(linha.OrganizationId),
                linha.Name,
                linha.Prefix,
                Instante(linha.CreatedOn),
                Opcional(linha.RevokedOn),
                Opcional(linha.LastUsedOn)));
        }

        return chaves;
    }

    public IssuedMachineKey Issue(Guid id, Guid organizationId, string name, DateTimeOffset now)
    {
        var secret = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        var prefixo = secret.Substring(0, 8);
        using var conexao = Abrir();
        conexao.Execute(
            "INSERT INTO office_machine_key (id, organization_id, name, prefix, secret_hash, created_on, revoked_on, last_used_on) VALUES (@id, @organizationId, @name, @prefix, @hash, @created, '', '')",
            new
            {
                id = id.ToString("D"),
                organizationId = organizationId.ToString("D"),
                name = name.Trim(),
                prefix = prefixo,
                hash = Hash(secret),
                created = Texto(now),
            });
        return new IssuedMachineKey(id, prefixo, secret);
    }

    public void Rename(Guid id, string name)
    {
        using var conexao = Abrir();
        conexao.Execute(
            "UPDATE office_machine_key SET name = @name WHERE id = @id AND revoked_on = ''",
            new { id = id.ToString("D"), name = name.Trim() });
    }

    public void Revoke(Guid id, DateTimeOffset now)
    {
        using var conexao = Abrir();
        conexao.Execute(
            "UPDATE office_machine_key SET revoked_on = @revoked WHERE id = @id",
            new { id = id.ToString("D"), revoked = Texto(now) });
    }

    public bool Touch(string presented, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var hash = Hash(presented.Trim());
        using var conexao = Abrir();
        var linha = conexao.QuerySingleOrDefault<LinhaDeHash>(
            "SELECT id AS Id, secret_hash AS SecretHash FROM office_machine_key WHERE revoked_on = '' AND secret_hash = @hash",
            new { hash });
        if (linha is null || !Fixed(linha.SecretHash, hash))
        {
            return false;
        }

        conexao.Execute(
            "UPDATE office_machine_key SET last_used_on = @used WHERE id = @id",
            new { id = linha.Id, used = Texto(now) });
        return true;
    }

    private void Gravar(string inserir, string atualizar, object valores)
    {
        using var conexao = Abrir();
        try
        {
            conexao.Execute(inserir, valores);
        }
        catch (DbException excecao) when (_sessao.EhViolacaoDeChave(excecao))
        {
            conexao.Execute(atualizar, valores);
        }
    }

    private DbConnection Abrir()
    {
        return _sessao.AbrirAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Hash(string secret)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static bool Fixed(string expected, string presented)
    {
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(presented);
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string Texto(DateTimeOffset? instante)
    {
        if (instante is null)
        {
            return string.Empty;
        }

        return instante.Value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset Instante(string texto)
    {
        return DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static DateTimeOffset? Opcional(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        return Instante(texto);
    }

    private sealed class LinhaDeMembro
    {
        public string Id { get; set; } = string.Empty;

        public string OrganizationId { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;
    }

    private sealed class LinhaDeChave
    {
        public string Id { get; set; } = string.Empty;

        public string OrganizationId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Prefix { get; set; } = string.Empty;

        public string CreatedOn { get; set; } = string.Empty;

        public string RevokedOn { get; set; } = string.Empty;

        public string LastUsedOn { get; set; } = string.Empty;
    }

    private sealed class LinhaDeHash
    {
        public string Id { get; set; } = string.Empty;

        public string SecretHash { get; set; } = string.Empty;
    }

}
