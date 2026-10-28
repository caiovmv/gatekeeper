using gatekeeper.Governance;
using gatekeeper.Governance.Adapters;
using gatekeeper.Persistence;
using gatekeeper.Persistence.Adapters;
using Xunit;

namespace gatekeeper.Tests.Governance;

public class GovernanceClosureTests
{
    public GovernanceClosureTests()
    {
        RegistroDeProvedores.Registrar(ProvedoresConhecidos.Sqlite);
    }

    [Fact]
    public void Oidc_rejects_a_mismatched_transaction_and_a_staff_issuer_for_a_user()
    {
        var now = DateTimeOffset.UtcNow;
        var rejected = OidcChecks.Validate(Sample(now, "other-state"));
        Assert.False(rejected.Ok);
        var staff = OidcChecks.Validate(Sample(now, "state-1") with { StaffIssuer = true, IdentityType = "user" });
        Assert.False(staff.Ok);
        Assert.True(OidcChecks.Validate(Sample(now, "state-1") with { IdentityType = "staff", StaffIssuer = true }).Ok);
    }

    [Fact]
    public void Mutation_budget_closes_the_sixty_first_admin_call_and_key_extension_needs_approval()
    {
        var budget = new MutationBudget();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 60; index++)
        {
            Assert.True(budget.Allow("admin", "203.0.113.8", 60, TimeSpan.FromMinutes(1), now));
        }

        Assert.False(budget.Allow("admin", "203.0.113.8", 60, TimeSpan.FromMinutes(1), now));
        var ledger = new GovernanceLedger("pepper");
        var organization = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var app = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(organization, "North").Ok);
        Assert.True(ledger.CreateTenant(tenant, organization, "Plant").Ok);
        Assert.True(ledger.CreateApp(app, tenant, "Desk").Ok);
        Assert.True(ledger.IssueApiKey(app, out _, out var prefix).Ok);
        Assert.False(ledger.ExtendApiKey(prefix, 120, false).Ok);
        var approval = Guid.NewGuid();
        var requester = Guid.NewGuid();
        var approver = Guid.NewGuid();
        Assert.True(ledger.RequestApproval(approval, requester, now).Ok);
        Assert.True(ledger.Approve(approval, approver, now).Ok);
        Assert.True(ledger.ExtendApiKey(prefix, 120, true).Ok);
        Assert.False(ledger.ExtendApiKey(prefix, 400, true).Ok);
    }

    [Fact]
    public void Pending_call_age_is_published_and_digest_mismatch_is_detectable()
    {
        var ledger = new GovernanceLedger("pepper");
        var ingested = Guid.NewGuid();
        Assert.True(ledger.IngestCall(ingested, Guid.NewGuid(), null, "sip").Ok);
        Assert.Equal(1, ledger.PendingCalls());
        Assert.True(ledger.ActivateDigest("digest-1").Ok);
        Assert.False(ledger.DigestMatches("other"));
        Assert.True(ledger.DigestMatches("digest-1"));
    }

    [Fact]
    public void Saml_rejects_a_wrapped_assertion_and_a_foreign_certificate()
    {
        var xml = Assertion("cert-a", "aud", "req-1", "https://gatekeeper/acs", "https://gatekeeper/acs");
        Assert.True(SamlChecks.Validate(xml, "aud", "req-1", "https://gatekeeper/acs", "https://gatekeeper/acs", "cert-a").Ok);
        Assert.False(SamlChecks.Validate(xml, "aud", "req-1", "https://gatekeeper/acs", "https://gatekeeper/acs", "cert-b").Ok);
        Assert.False(SamlChecks.Validate(xml + xml, "aud", "req-1", "https://gatekeeper/acs", "https://gatekeeper/acs", "cert-a").Ok);
    }

    [Fact]
    public void Login_throttle_closes_after_ten_attempts_and_idle_session_expires()
    {
        var throttle = new LoginThrottle();
        var now = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.True(throttle.Allow("127.0.0.1", "admin@localhost", now));
        }

        Assert.False(throttle.Allow("127.0.0.1", "admin@localhost", now));
        var clock = new SessionClock();
        Assert.False(clock.Expired("staff-1", now));
        Assert.True(clock.Expired("staff-1", now.AddMinutes(31)));
    }

    [Fact]
    public void Rotation_break_glass_suspension_and_digest_follow_the_contract()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var app = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.CreateTenant(tenant, org, "Brasil").Ok);
        Assert.True(ledger.CreateApp(app, tenant, "Atendimento").Ok);
        Assert.True(ledger.IssueApiKey(app, out var secret, out var prefix).Ok);
        Assert.True(ledger.RotateApiKey(prefix, out var nextSecret, out var nextPrefix).Ok);
        Assert.True(ledger.AuthenticateApiKey(prefix, secret, app).Ok);
        Assert.True(ledger.AuthenticateApiKey(nextPrefix, nextSecret, app).Ok);
        var now = DateTimeOffset.UtcNow;
        var first = Guid.NewGuid();
        Assert.False(ledger.OpenBreakGlass(Guid.NewGuid(), first, first, now).Ok);
        var glass = Guid.NewGuid();
        Assert.True(ledger.OpenBreakGlass(glass, first, Guid.NewGuid(), now).Ok);
        Assert.False(ledger.OpenBreakGlass(glass, first, Guid.NewGuid(), now).Ok);
        Assert.True(ledger.SuspendTenant(tenant).Ok);
        Assert.False(ledger.CreateApp(Guid.NewGuid(), tenant, "Novo").Ok);
        Assert.True(ledger.Hold("call", now.AddDays(10)).Ok);
        Assert.False(ledger.MayPurge("call", now.AddDays(-100), now));
        Assert.False(ledger.DigestMatches("abc"));
        Assert.True(ledger.ActivateDigest("abc").Ok);
        Assert.True(ledger.DigestMatches("abc"));
    }

    [Fact]
    public async Task Sql_commit_rolls_back_the_audit_and_the_outbox_together()
    {
        var caminho = Path.Combine(Path.GetTempPath(), "gatekeeper-governance-" + Guid.NewGuid().ToString("N") + ".db");
        var opcoes = new OpcoesDeBanco(ProvedoresConhecidos.Sqlite, "Data Source=" + caminho + ";Pooling=False");
        var sql = new GovernanceSql(new SessaoSql(new FabricaDeConexao(opcoes), DialetosSql.Para(opcoes.ProvedorInvariante)));
        try
        {
            var org = Guid.NewGuid();
            Assert.True(await sql.CommitOrganizationAsync(org, "Cliente", Guid.NewGuid(), CancellationToken.None));
            Assert.False(await sql.CommitOrganizationAsync(org, "Outro", Guid.NewGuid(), CancellationToken.None));
            Assert.Equal(1, await sql.CountAsync("organization", CancellationToken.None));
            Assert.Equal(1, await sql.CountAsync("audit_event", CancellationToken.None));
            Assert.Equal(1, await sql.CountAsync("caixa_saida", CancellationToken.None));
        }
        finally
        {
            if (File.Exists(caminho))
            {
                File.Delete(caminho);
            }
        }
    }

    private static OidcLogin Sample(DateTimeOffset now, string state)
    {
        return new OidcLogin("code", state, "state-1", "nonce-1", "nonce-1", "https://staff", "https://staff", "gatekeeper-console", "gatekeeper-console", "S256", now.AddMinutes(5), now, false, "staff");
    }

    private static string Assertion(string certificate, string audience, string inResponseTo, string destination, string recipient)
    {
        return "<Response InResponseTo=\"" + inResponseTo + "\" Destination=\"" + destination + "\"><Assertion ID=\"a1\"><Signature><SignedInfo><Reference URI=\"#a1\"/></SignedInfo><X509Certificate>" + certificate + "</X509Certificate></Signature><Audience>" + audience + "</Audience><SubjectConfirmationData Recipient=\"" + recipient + "\"/></Assertion></Response>";
    }
}
