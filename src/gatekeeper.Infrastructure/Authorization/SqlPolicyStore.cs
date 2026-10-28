using System.Globalization;
using gatekeeper.Persistence.Adapters;
using Dapper;

namespace gatekeeper.Authorization;

public sealed class SqlPolicyStore : IPolicyStore
{
    private readonly SessaoSql _sessao;

    public SqlPolicyStore(SessaoSql sessao)
    {
        _sessao = sessao;
    }

    public IReadOnlyList<AccessRule> Rules()
    {
        GarantirSemente();
        return Ler().Where(rule => rule.RemovedOn is null).ToArray();
    }

    public IReadOnlyList<AccessRule> History()
    {
        GarantirSemente();
        return Ler();
    }

    public void Save(AccessRule rule)
    {
        Gravar(
            "INSERT INTO access_rule (id, resource, role, action, effect, removed_on) VALUES (@id, @resource, @role, @action, @effect, @removed)",
            "UPDATE access_rule SET resource = @resource, role = @role, action = @action, effect = @effect, removed_on = @removed WHERE id = @id",
            new
            {
                id = rule.Id.ToString("D"),
                resource = rule.Resource,
                role = rule.Role,
                action = rule.Action,
                effect = rule.Effect,
                removed = Texto(rule.RemovedOn),
            });
    }

    public void Remove(Guid id, DateTimeOffset now)
    {
        using var conexao = Abrir();
        conexao.Execute(
            "UPDATE access_rule SET removed_on = @removed WHERE id = @id AND removed_on = ''",
            new { id = id.ToString("D"), removed = Texto(now) });
    }

    public bool Allows(IReadOnlyList<string> roles, string action, string resource)
    {
        var allowed = false;
        foreach (var rule in Rules())
        {
            if (rule.Resource != resource || rule.Action != action || !roles.Contains(rule.Role))
            {
                continue;
            }

            if (rule.Effect == "deny")
            {
                return false;
            }

            if (rule.Effect == "allow")
            {
                allowed = true;
            }
        }

        return allowed;
    }

    private void GarantirSemente()
    {
        long quantidade;
        using (var conexao = Abrir())
        {
            quantidade = conexao.ExecuteScalar<long>("SELECT COUNT(*) FROM access_rule");
        }

        if (quantidade > 0)
        {
            return;
        }

        Conceder(new[] { "Staff.Admin", "admin" }, new[] { "plan", "subscription", "organization", "member", "machine-key", "team", "team-member", "policy" }, new[] { "create", "update", "delete", "read" });
        Conceder(new[] { "Staff.Admin", "admin", "Staff.Support", "support" }, new[] { "organization", "member", "team", "team-member" }, new[] { "read" });
        Conceder(new[] { "Staff.Admin", "admin", "Staff.Support", "support", "Staff.Auditor", "auditor" }, new[] { "audit", "access-log" }, new[] { "read" });
        Conceder(new[] { "Staff.Admin", "admin" }, new[] { "audit", "access-log" }, new[] { "ingest" });
    }

    private void Conceder(string[] roles, string[] resources, string[] actions)
    {
        var atuais = Ler();
        foreach (var role in roles)
        {
            foreach (var resource in resources)
            {
                foreach (var action in actions)
                {
                    var existe = false;
                    foreach (var rule in atuais)
                    {
                        if (rule.Role == role && rule.Resource == resource && rule.Action == action)
                        {
                            existe = true;
                        }
                    }

                    if (existe)
                    {
                        continue;
                    }

                    var regra = new AccessRule(Guid.NewGuid(), resource, role, action, "allow", null);
                    Save(regra);
                    atuais.Add(regra);
                }
            }
        }
    }

    private List<AccessRule> Ler()
    {
        using var conexao = Abrir();
        var linhas = conexao.Query<Linha>("SELECT id AS Id, resource AS Resource, role AS Role, action AS Action, effect AS Effect, removed_on AS RemovedOn FROM access_rule");
        var regras = new List<AccessRule>();
        foreach (var linha in linhas)
        {
            DateTimeOffset? removida = string.IsNullOrEmpty(linha.RemovedOn) ? null : DateTimeOffset.Parse(linha.RemovedOn, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            regras.Add(new AccessRule(Guid.Parse(linha.Id), linha.Resource, linha.Role, linha.Action, linha.Effect, removida));
        }

        return regras;
    }

    private void Gravar(string inserir, string atualizar, object valores)
    {
        using var conexao = Abrir();
        try
        {
            conexao.Execute(inserir, valores);
        }
        catch (System.Data.Common.DbException excecao) when (_sessao.EhViolacaoDeChave(excecao))
        {
            conexao.Execute(atualizar, valores);
        }
    }

    private System.Data.Common.DbConnection Abrir()
    {
        return _sessao.AbrirAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Texto(DateTimeOffset? instante)
    {
        if (instante is null)
        {
            return string.Empty;
        }

        return instante.Value.ToString("O", CultureInfo.InvariantCulture);
    }

    private sealed class Linha
    {
        public string Id { get; set; } = string.Empty;

        public string Resource { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string Effect { get; set; } = string.Empty;

        public string RemovedOn { get; set; } = string.Empty;
    }
}
