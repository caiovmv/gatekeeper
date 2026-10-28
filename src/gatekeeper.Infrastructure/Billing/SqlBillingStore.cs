using System.Data.Common;
using System.Globalization;
using gatekeeper.Persistence.Adapters;
using Dapper;

namespace gatekeeper.Billing;

public sealed class SqlBillingStore : IBillingStore
{
    private readonly SessaoSql _sessao;

    public SqlBillingStore(SessaoSql sessao)
    {
        _sessao = sessao;
    }

    public IReadOnlyList<Organization> Organizations()
    {
        using var conexao = Abrir();
        var linhas = conexao.Query<LinhaDeOrganizacao>(
            "SELECT id AS Id, name AS Name, host AS Host, idp_issuer AS IdpIssuer, directory_id AS DirectoryId FROM office_organization WHERE removed_on = ''");
        var organizacoes = new List<Organization>();
        foreach (var linha in linhas)
        {
            organizacoes.Add(new Organization(Guid.Parse(linha.Id), linha.Name, linha.Host, linha.IdpIssuer, linha.DirectoryId));
        }

        return organizacoes;
    }

    public void Save(Organization organization)
    {
        Gravar(
            "INSERT INTO office_organization (id, name, host, idp_issuer, directory_id, removed_on) VALUES (@id, @name, @host, @idpIssuer, @directoryId, @removed)",
            "UPDATE office_organization SET name = @name, host = @host, idp_issuer = @idpIssuer, directory_id = @directoryId, removed_on = @removed WHERE id = @id",
            new
            {
                id = organization.Id.ToString("D"),
                name = organization.Name,
                host = organization.Host,
                idpIssuer = organization.IdpIssuer,
                directoryId = organization.DirectoryId,
                removed = Texto(organization.RemovedOn),
            });
    }

    public void RemoveOrganization(Guid id)
    {
        MarcarRemovido("office_organization", id);
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

    private void MarcarRemovido(string tabela, Guid id)
    {
        using var conexao = Abrir();
        conexao.Execute(
            "UPDATE " + tabela + " SET removed_on = @removed WHERE id = @id AND removed_on = ''",
            new { id = id.ToString("D"), removed = DateTimeOffset.UtcNow.ToString("O") });
    }

    private DbConnection Abrir()
    {
        return _sessao.AbrirAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Texto(DateTimeOffset? instante)
    {
        if (instante is null)
        {
            return string.Empty;
        }

        return instante.Value.ToString("O");
    }

    private sealed class LinhaDeOrganizacao
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Host { get; set; } = string.Empty;

        public string IdpIssuer { get; set; } = string.Empty;

        public string DirectoryId { get; set; } = string.Empty;
    }
}
