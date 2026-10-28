using gatekeeper.Governance;
using Xunit;

namespace gatekeeper.Tests.Governance;

public class GovernanceTests
{
    [Fact]
    public void Tenant_sibling_cannot_bill_the_other_apps_scope()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        var brasil = Guid.NewGuid();
        var mexico = Guid.NewGuid();
        var atendimento = Guid.NewGuid();
        var vendas = Guid.NewGuid();
        var user = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.CreateTenant(brasil, org, "Brasil").Ok);
        Assert.True(ledger.CreateTenant(mexico, org, "Mexico").Ok);
        Assert.True(ledger.CreateApp(atendimento, brasil, "Atendimento").Ok);
        Assert.True(ledger.CreateApp(vendas, mexico, "Atendimento").Ok);
        Assert.True(ledger.Grant(user, atendimento).Ok);
        Assert.False(ledger.Bill(org, mexico, vendas, "evt-1", 10, user).Ok);
        Assert.Equal(0, ledger.BilledAmount);
    }

    [Fact]
    public void Renaming_an_app_keeps_the_tenant()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var app = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.CreateTenant(tenant, org, "Brasil").Ok);
        Assert.True(ledger.CreateApp(app, tenant, "Atendimento").Ok);
        Assert.True(ledger.RenameApp(app, "CustomerCare").Ok);
        Assert.True(ledger.SameTenant(app, tenant));
    }

    [Fact]
    public void External_link_requires_proof_and_rejects_staff_issuer_for_a_user()
    {
        var ledger = new GovernanceLedger();
        var identity = Guid.NewGuid();
        var provider = Guid.NewGuid();
        Assert.False(ledger.LinkExternal(identity, "user", provider, "https://tenant.example", "sub-1", false).Ok);
        Assert.False(ledger.LinkExternal(identity, "user", provider, "https://staff.example", "sub-1", true).Ok);
        Assert.True(ledger.LinkExternal(identity, "user", provider, "https://tenant.example", "sub-1", true).Ok);
        Assert.False(ledger.LinkExternal(Guid.NewGuid(), "user", provider, "https://tenant.example", "sub-1", true).Ok);
    }

    [Fact]
    public void Api_key_is_hashed_scoped_and_revocation_survives_recovery()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var app = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.CreateTenant(tenant, org, "Brasil").Ok);
        Assert.True(ledger.CreateApp(app, tenant, "Atendimento").Ok);
        var issued = ledger.IssueApiKey(app, out var secret, out var prefix);
        Assert.True(issued.Ok);
        Assert.True(ledger.SecretStored(prefix));
        Assert.True(ledger.AuthenticateApiKey(prefix, secret, app).Ok);
        Assert.False(ledger.AuthenticateApiKey(prefix, secret, Guid.NewGuid()).Ok);
        Assert.True(ledger.RevokeApiKey(prefix).Ok);
        Assert.False(ledger.AuthenticateApiKey(prefix, secret, app).Ok);
        Assert.True(ledger.EnterRecovery(new[] { prefix }).Ok);
        Assert.True(ledger.Lockdown);
        Assert.False(ledger.AuthenticateApiKey(prefix, secret, app).Ok);
        Assert.True(ledger.SecurityEvents > 0);
    }

    [Fact]
    public void Revoke_and_bill_serialize_and_duplicates_do_not_charge_twice()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var app = Guid.NewGuid();
        var user = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.CreateTenant(tenant, org, "Brasil").Ok);
        Assert.True(ledger.CreateApp(app, tenant, "Atendimento").Ok);
        Assert.True(ledger.Grant(user, app).Ok);
        Assert.True(ledger.Bill(org, tenant, app, "evt-1", 5, user).Ok);
        Assert.True(ledger.Bill(org, tenant, app, "evt-1", 5, user).Ok);
        Assert.Equal(5, ledger.BilledAmount);
        Assert.True(ledger.RevokeGrant(user, app).Ok);
        Assert.False(ledger.Bill(org, tenant, app, "evt-2", 9, user).Ok);
        Assert.Equal(5, ledger.BilledAmount);
    }

    [Fact]
    public void Pending_call_is_not_billable_and_unknown_external_waits_for_reconciliation()
    {
        var ledger = new GovernanceLedger();
        var call = Guid.NewGuid();
        var operation = Guid.NewGuid();
        Assert.True(ledger.IngestCall(call, null, null, "external_verified").Ok);
        Assert.False(ledger.Billable(call));
        Assert.True(ledger.RequestExternal(operation).Ok);
        Assert.True(ledger.UnknownExternal(operation, false).Ok);
        Assert.Equal("reconciliation_required", ledger.ExternalState(operation));
    }

    [Fact]
    public void Rewritten_partition_fails_when_the_anchor_cannot_be_replaced()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.VerifyPartition("organization"));
        var store = new MemoryAnchorStore();
        Assert.True(ledger.Anchor("organization", store).Ok);
        Assert.False(store.TryReplace("organization", "forged"));
        Assert.False(ledger.Anchor("organization", store).Ok);
    }

    [Fact]
    public void Compliance_lock_refuses_version_delete_and_shorter_retention()
    {
        var until = DateTimeOffset.UtcNow.AddYears(5);
        Assert.False(ObjectLockRules.CanDeleteVersion(true, until, DateTimeOffset.UtcNow, true));
        Assert.False(ObjectLockRules.CanShortenRetention(true, until, until.AddDays(-1)));
        Assert.True(ObjectLockRules.CanShortenRetention(true, until, until.AddDays(1)));
    }

    [Fact]
    public void Absent_profile_is_restricted_and_production_deployment_rejects_local_profiles()
    {
        Assert.Equal(GovernanceMode.Restricted, GovernanceEnvironment.Resolve(null, null, true));
        Assert.False(GovernanceEnvironment.AllowsLocalBootstrap(GovernanceMode.Restricted));
        Assert.Equal(GovernanceMode.Local, GovernanceEnvironment.Resolve("leve", "development", true));
        Assert.Equal(GovernanceMode.Restricted, GovernanceEnvironment.Resolve("leve", "development", false));
        Assert.Equal(GovernanceMode.Abort, GovernanceEnvironment.Resolve("leve", "producao", true));
        Assert.Equal(GovernanceMode.Abort, GovernanceEnvironment.Resolve("teste", "homologacao", false));
        Assert.Equal(GovernanceMode.Federated, GovernanceEnvironment.Resolve("producao", "producao", false));
        Assert.True(GovernanceEnvironment.RequiresDurableAudit(GovernanceMode.Federated));
    }

    [Fact]
    public void Staff_without_assignment_cannot_read_the_organization()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        var other = Guid.NewGuid();
        var staff = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.CreateOrganization(other, "Outro").Ok);
        Assert.True(ledger.AssignStaff(staff, org).Ok);
        Assert.True(ledger.StaffCanRead(staff, org));
        Assert.False(ledger.StaffCanRead(staff, other));
    }

    [Fact]
    public void Schema_declares_foreign_keys_and_a_stable_external_subject()
    {
        Assert.Contains("REFERENCES organization(id)", GovernanceSchema.PostgreSql, StringComparison.Ordinal);
        Assert.Contains("REFERENCES tenant(id)", GovernanceSchema.PostgreSql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (provider, integration_id, source_event_id)", GovernanceSchema.PostgreSql, StringComparison.Ordinal);
        Assert.Contains("tenant_owner", GovernanceSchema.PostgreSql, StringComparison.Ordinal);
        Assert.False(GovernanceProof.WormApproved);
        Assert.False(GovernanceProof.SloMeasured);
        Assert.False(GovernanceProof.RestoreDrillPassed);
    }

    [Fact]
    public void App_cannot_move_and_same_idempotency_key_with_another_body_conflicts()
    {
        var ledger = new GovernanceLedger();
        var org = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        var app = Guid.NewGuid();
        Assert.True(ledger.CreateOrganization(org, "Cliente").Ok);
        Assert.True(ledger.CreateTenant(tenant, org, "Brasil").Ok);
        Assert.True(ledger.CreateTenant(other, org, "Mexico").Ok);
        Assert.True(ledger.CreateApp(app, tenant, "Atendimento").Ok);
        Assert.False(ledger.MoveApp(app, other).Ok);
        Assert.True(ledger.SameTenant(app, tenant));
        Assert.True(ledger.AcceptIdempotency("key-1", "a").Ok);
        Assert.Equal("duplicate", ledger.AcceptIdempotency("key-1", "a").Detail);
        Assert.False(ledger.AcceptIdempotency("key-1", "b").Ok);
    }

    [Fact]
    public void Approval_expires_and_the_requester_cannot_approve()
    {
        var ledger = new GovernanceLedger();
        var id = Guid.NewGuid();
        var requester = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.True(ledger.RequestApproval(id, requester, now).Ok);
        Assert.False(ledger.Approve(id, requester, now).Ok);
        Assert.True(ledger.Approve(id, approver, now).Ok);
        Assert.False(ledger.Approve(id, approver, now).Ok);
        Assert.False(RetentionDefaults.MayPurge("invoice", now, now.AddYears(1), false));
        Assert.False(RetentionDefaults.MayPurge("call", now.AddDays(-100), now, true));
        Assert.Equal("a***@localhost", PiiMask.Email("admin@localhost"));
    }

    [Fact]
    public void Checkpoint_older_than_five_minutes_blocks_admin_writes()
    {
        var ledger = new GovernanceLedger();
        var now = DateTimeOffset.UtcNow;
        ledger.NoteCheckpoint(now.AddMinutes(-6), now);
        Assert.True(ledger.CheckpointAlert);
        Assert.False(ledger.CreateOrganization(Guid.NewGuid(), "Cliente").Ok);
    }

    [Fact]
    public void Pending_call_can_be_attributed_and_only_then_becomes_billable()
    {
        var ledger = new GovernanceLedger();
        var call = Guid.NewGuid();
        Assert.True(ledger.IngestCall(call, null, null, "sip").Ok);
        Assert.False(ledger.Billable(call));
        Assert.True(ledger.AttributeCall(call, "disputed").Ok);
        Assert.False(ledger.Billable(call));
        Assert.True(ledger.AttributeCall(call, "rejected").Ok);
        Assert.False(ledger.AttributeCall(call, "attributed").Ok);
    }
}
