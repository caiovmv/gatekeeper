using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace gatekeeper.Governance;

public enum GovernanceMode
{
    Restricted,
    Local,
    Test,
    Federated,
    Abort,
}

public static class GovernanceEnvironment
{
    public static GovernanceMode Resolve(string? profile, string? deployment, bool allowLocalBootstrap)
    {
        var name = Normalize(profile);
        var place = Normalize(deployment);
        var productionPlace = place == "producao" || place == "homologacao" || place == "production" || place == "staging";
        if (name.Length == 0)
        {
            return productionPlace ? GovernanceMode.Abort : GovernanceMode.Restricted;
        }

        if (name == "leve")
        {
            if (productionPlace)
            {
                return GovernanceMode.Abort;
            }

            return allowLocalBootstrap ? GovernanceMode.Local : GovernanceMode.Restricted;
        }

        if (name == "teste")
        {
            return productionPlace ? GovernanceMode.Abort : GovernanceMode.Test;
        }

        if (name == "homologacao" || name == "producao")
        {
            return GovernanceMode.Federated;
        }

        return GovernanceMode.Abort;
    }

    public static bool AllowsLocalBootstrap(GovernanceMode mode)
    {
        return mode == GovernanceMode.Local || mode == GovernanceMode.Test;
    }

    public static bool RequiresDurableAudit(GovernanceMode mode)
    {
        return mode == GovernanceMode.Federated;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}

public sealed record GovernanceOutcome(bool Ok, string Detail);

public sealed record TenantRecord(Guid Id, Guid OrganizationId, string Name, string Status);

public sealed record AppRecord(Guid Id, Guid OrganizationId, Guid TenantId, string Name, string Status);

public sealed record ExternalRecord(Guid Id, string State);

public sealed class GovernanceLedger
{
    private readonly Ledger _ledger = new();
    private readonly byte[] _pepper;

    public GovernanceLedger()
        : this("governance-key-pepper")
    {
    }

    public GovernanceLedger(string pepper)
    {
        var text = string.IsNullOrWhiteSpace(pepper) ? "governance-key-pepper" : pepper.Trim();
        _pepper = Encoding.UTF8.GetBytes(text);
    }

    public GovernanceOutcome CreateOrganization(Guid id, string name)
    {
        return Mutate(delegate
        {
            if (_ledger.Organizations.ContainsKey(id))
            {
                return Fail("organization exists");
            }

            _ledger.Organizations[id] = new Org(id, name, "active");
            return Commit("organization", id, "create");
        });
    }

    public GovernanceOutcome CreateTenant(Guid id, Guid organizationId, string name)
    {
        return Mutate(delegate
        {
            if (!_ledger.Organizations.TryGetValue(organizationId, out var org) || org.Status != "active")
            {
                return Fail("organization missing");
            }

            _ledger.Tenants[id] = new TenantRow(id, organizationId, name, "active");
            return Commit("tenant", id, "create");
        });
    }

    public GovernanceOutcome CreateApp(Guid id, Guid tenantId, string name)
    {
        return Mutate(delegate
        {
            if (!_ledger.Tenants.TryGetValue(tenantId, out var tenant) || tenant.Status != "active")
            {
                return Fail("tenant missing");
            }

            _ledger.Apps[id] = new AppRow(id, tenant.OrganizationId, tenantId, name, "active");
            return Commit("app", id, "create");
        });
    }

    public GovernanceOutcome RenameApp(Guid id, string name)
    {
        return Mutate(delegate
        {
            if (!_ledger.Apps.TryGetValue(id, out var app))
            {
                return Fail("app missing");
            }

            _ledger.Apps[id] = app with { Name = name };
            return Commit("app", id, "rename");
        });
    }

    public bool SameTenant(Guid appId, Guid tenantId)
    {
        lock (_ledger.Gate)
        {
            return _ledger.Apps.TryGetValue(appId, out var app) && app.TenantId == tenantId;
        }
    }

    public GovernanceOutcome LinkExternal(Guid identityId, string identityType, Guid providerId, string issuer, string subject, bool proofOfControl)
    {
        return Mutate(delegate
        {
            if (!proofOfControl)
            {
                return Fail("proof required");
            }

            if (identityType == "user" && issuer.Contains("staff", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("tenant identity cannot become staff");
            }

            var key = providerId.ToString("N") + "|" + subject;
            if (_ledger.ExternalSubjects.Contains(key))
            {
                return Fail("subject already linked");
            }

            _ledger.ExternalSubjects.Add(key);
            _ledger.Identities[identityId] = identityType;
            return Commit("identity", identityId, "link");
        });
    }

    public GovernanceOutcome IssueApiKey(Guid appId, out string secret, out string prefix)
    {
        secret = string.Empty;
        prefix = string.Empty;
        var bytes = RandomNumberGenerator.GetBytes(32);
        var shown = Convert.ToHexString(bytes).ToLowerInvariant();
        var issuedPrefix = shown.Substring(0, 8);
        var hash = Verifier(shown);
        var outcome = Mutate(delegate
        {
            if (!_ledger.Apps.ContainsKey(appId))
            {
                return Fail("app missing");
            }

            _ledger.Keys[issuedPrefix] = new KeyRow(appId, hash, DateTimeOffset.UtcNow.AddDays(90), null, false);
            return Commit("api-key", appId, "issue");
        });
        if (!outcome.Ok)
        {
            return outcome;
        }

        secret = shown;
        prefix = issuedPrefix;
        return outcome;
    }

    public bool SecretStored(string prefix)
    {
        lock (_ledger.Gate)
        {
            return _ledger.Keys.TryGetValue(prefix, out var key) && key.Hash.Length == 64;
        }
    }

    public GovernanceOutcome AuthenticateApiKey(string prefix, string secret, Guid appId)
    {
        lock (_ledger.Gate)
        {
            if (_ledger.Lockdown)
            {
                return Fail("recovery lockdown");
            }

            if (!_ledger.Keys.TryGetValue(prefix, out var key))
            {
                return Security("unknown", "api-key", "denied");
            }

            if (key.Quarantined || key.RevokedAt is not null || key.ExpiresAt <= DateTimeOffset.UtcNow || key.AppId != appId)
            {
                return Security("unknown", "api-key", "denied");
            }

            var hash = Verifier(secret);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(key.Hash)))
            {
                return Security("unknown", "api-key", "denied");
            }

            return new GovernanceOutcome(true, "authenticated");
        }
    }

    public GovernanceOutcome RevokeApiKey(string prefix)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = Mutate(delegate
        {
            if (!_ledger.Keys.TryGetValue(prefix, out var key))
            {
                return Fail("key missing");
            }

            _ledger.Keys[prefix] = key with { RevokedAt = DateTimeOffset.UtcNow };
            _ledger.RevokedPrefixes.Add(prefix);
            _ledger.Epoch++;
            return Commit("api-key", key.AppId, "revoke");
        });
        GovernanceMeters.RevocationLatency.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
        return outcome;
    }

    public GovernanceOutcome AssignStaff(Guid identityId, Guid organizationId)
    {
        return Mutate(delegate
        {
            _ledger.StaffAssignments.Add(identityId.ToString("N") + "|" + organizationId.ToString("N"));
            return Commit("assignment", identityId, "assign");
        });
    }

    public bool StaffCanRead(Guid identityId, Guid organizationId)
    {
        lock (_ledger.Gate)
        {
            return _ledger.StaffAssignments.Contains(identityId.ToString("N") + "|" + organizationId.ToString("N"));
        }
    }

    public GovernanceOutcome RevokeGrant(Guid identityId, Guid scopeId)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = Mutate(delegate
        {
            _ledger.Grants.Remove(identityId.ToString("N") + "|" + scopeId.ToString("N"));
            _ledger.Epoch++;
            return Commit("grant", scopeId, "revoke");
        });
        GovernanceMeters.RevocationLatency.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
        return outcome;
    }

    public GovernanceOutcome Grant(Guid identityId, Guid scopeId)
    {
        return Mutate(delegate
        {
            _ledger.Grants.Add(identityId.ToString("N") + "|" + scopeId.ToString("N"));
            return Commit("grant", scopeId, "allow");
        });
    }

    public GovernanceOutcome Bill(Guid organizationId, Guid tenantId, Guid appId, string eventId, decimal amount, Guid identityId)
    {
        return Mutate(delegate
        {
            if (!_ledger.Apps.TryGetValue(appId, out var app) || app.TenantId != tenantId || app.OrganizationId != organizationId || app.Status != "active")
            {
                return Fail("scope mismatch");
            }

            var grant = identityId.ToString("N") + "|" + appId.ToString("N");
            if (!_ledger.Grants.Contains(grant))
            {
                return Security(identityId.ToString("N"), "usage", "denied");
            }

            var dedup = organizationId.ToString("N") + "|" + eventId;
            if (_ledger.BilledEvents.Contains(dedup))
            {
                _ledger.BillingDuplicates++;
                GovernanceMeters.BillingDedup.Add(1);
                return new GovernanceOutcome(true, "duplicate");
            }

            _ledger.BilledEvents.Add(dedup);
            _ledger.BilledAmount += amount;
            return Commit("usage", appId, "bill");
        });
    }

    public decimal BilledAmount
    {
        get
        {
            lock (_ledger.Gate)
            {
                return _ledger.BilledAmount;
            }
        }
    }

    public GovernanceOutcome IngestCall(Guid id, Guid? tenantId, Guid? appId, string origin)
    {
        return Mutate(delegate
        {
            var status = appId is null ? "pending" : "attributed";
            _ledger.Calls[id] = new CallRow(id, tenantId, appId, origin, status, DateTimeOffset.UtcNow);
            PublishCallAge(DateTimeOffset.UtcNow);
            return Commit("call", id, "ingest");
        });
    }

    public bool Billable(Guid callId)
    {
        lock (_ledger.Gate)
        {
            return _ledger.Calls.TryGetValue(callId, out var call) && call.Status == "attributed" && call.AppId is not null;
        }
    }

    public GovernanceOutcome RequestExternal(Guid id)
    {
        return Mutate(delegate
        {
            _ledger.External[id] = "requested";
            return Commit("external", id, "request");
        });
    }

    public GovernanceOutcome UnknownExternal(Guid id, bool idempotent)
    {
        return Mutate(delegate
        {
            if (!_ledger.External.ContainsKey(id))
            {
                return Fail("missing");
            }

            _ledger.External[id] = idempotent ? "dispatching" : "reconciliation_required";
            if (!idempotent)
            {
                _ledger.Reconciliations++;
                GovernanceMeters.ReconciliationRequired.Add(1);
            }
            return Commit("external", id, "unknown");
        });
    }

    public string ExternalState(Guid id)
    {
        lock (_ledger.Gate)
        {
            return _ledger.External.TryGetValue(id, out var state) ? state : string.Empty;
        }
    }

    public GovernanceOutcome MoveApp(Guid appId, Guid tenantId)
    {
        if (SameTenant(appId, tenantId))
        {
            return Fail("app is not transferable");
        }

        return Security("unknown", "app", "denied");
    }

    public IReadOnlyList<TenantRecord> TenantsOf(Guid organizationId)
    {
        lock (_ledger.Gate)
        {
            var rows = new List<TenantRecord>();
            foreach (var tenant in _ledger.Tenants.Values)
            {
                if (tenant.OrganizationId == organizationId)
                {
                    rows.Add(new TenantRecord(tenant.Id, tenant.OrganizationId, tenant.Name, tenant.Status));
                }
            }

            return rows;
        }
    }

    public IReadOnlyList<AppRecord> AppsOf(Guid tenantId)
    {
        lock (_ledger.Gate)
        {
            var rows = new List<AppRecord>();
            foreach (var app in _ledger.Apps.Values)
            {
                if (app.TenantId == tenantId)
                {
                    rows.Add(new AppRecord(app.Id, app.OrganizationId, app.TenantId, app.Name, app.Status));
                }
            }

            return rows;
        }
    }

    public GovernanceOutcome AttributeCall(Guid id, string status)
    {
        return Mutate(delegate
        {
            if (!_ledger.Calls.TryGetValue(id, out var call))
            {
                return Fail("call missing");
            }

            var allowed = (call.Status == "pending" && (status == "attributed" || status == "disputed" || status == "rejected"))
                || (call.Status == "disputed" && (status == "attributed" || status == "rejected"));
            if (!allowed)
            {
                return Fail("transition refused");
            }

            _ledger.Calls[id] = call with { Status = status };
            PublishCallAge(DateTimeOffset.UtcNow);
            return Commit("call", id, status);
        });
    }

    public GovernanceOutcome AcceptIdempotency(string key, string fingerprint)
    {
        return Mutate(delegate
        {
            if (_ledger.Idempotency.TryGetValue(key, out var seen))
            {
                return seen == fingerprint ? new GovernanceOutcome(true, "duplicate") : Fail("conflict");
            }

            _ledger.Idempotency[key] = fingerprint;
            return new GovernanceOutcome(true, "accepted");
        });
    }

    public GovernanceOutcome RequestApproval(Guid id, Guid requester, DateTimeOffset now)
    {
        return Mutate(delegate
        {
            _ledger.Approvals[id] = new Approval(requester, now.AddMinutes(30), false);
            return Commit("approval", id, "request");
        });
    }

    public GovernanceOutcome Approve(Guid id, Guid approver, DateTimeOffset now)
    {
        return Mutate(delegate
        {
            if (!_ledger.Approvals.TryGetValue(id, out var approval) || approval.Consumed || approval.ExpiresAt < now || approval.Requester == approver)
            {
                return Fail("approval refused");
            }

            _ledger.Approvals[id] = approval with { Consumed = true };
            return Commit("approval", id, "approve");
        });
    }

    public GovernanceOutcome RotateApiKey(string prefix, out string secret, out string nextPrefix)
    {
        secret = string.Empty;
        nextPrefix = string.Empty;
        var shown = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var issuedPrefix = shown.Substring(0, 8);
        var hash = Verifier(shown);
        var outcome = Mutate(delegate
        {
            if (!_ledger.Keys.TryGetValue(prefix, out var key) || key.RevokedAt is not null)
            {
                return Fail("key missing");
            }

            var overlap = DateTimeOffset.UtcNow.AddHours(24);
            var expiry = key.ExpiresAt < overlap ? key.ExpiresAt : overlap;
            _ledger.Keys[prefix] = key with { ExpiresAt = expiry };
            _ledger.Keys[issuedPrefix] = new KeyRow(key.AppId, hash, DateTimeOffset.UtcNow.AddDays(90), null, false);
            return Commit("api-key", key.AppId, "rotate");
        });
        if (!outcome.Ok)
        {
            return outcome;
        }

        secret = shown;
        nextPrefix = issuedPrefix;
        return outcome;
    }

    public GovernanceOutcome ExtendApiKey(string prefix, int days, bool approved)
    {
        return Mutate(delegate
        {
            if (days < 1 || days > 365 || (days > 90 && !approved))
            {
                return Fail("approval refused");
            }

            if (!_ledger.Keys.TryGetValue(prefix, out var key) || key.RevokedAt is not null)
            {
                return Fail("key missing");
            }

            _ledger.Keys[prefix] = key with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(days) };
            return Commit("api-key", key.AppId, "extend");
        });
    }

    public IReadOnlyList<ExternalRecord> Integrations()
    {
        lock (_ledger.Gate)
        {
            var rows = new List<ExternalRecord>();
            foreach (var pair in _ledger.External)
            {
                rows.Add(new ExternalRecord(pair.Key, pair.Value));
            }

            return rows;
        }
    }

    public int PendingCalls()
    {
        lock (_ledger.Gate)
        {
            var count = 0;
            foreach (var call in _ledger.Calls.Values)
            {
                if (call.Status == "pending")
                {
                    count++;
                }
            }

            return count;
        }
    }

    public GovernanceOutcome OpenBreakGlass(Guid id, Guid first, Guid second, DateTimeOffset now)
    {
        return Mutate(delegate
        {
            if (first == second)
            {
                return Fail("approval refused");
            }

            if (_ledger.BreakGlass.TryGetValue(id, out var open) && open > now)
            {
                return Fail("break-glass active");
            }

            _ledger.BreakGlass[id] = now.AddMinutes(60);
            return Commit("break-glass", id, "open");
        });
    }

    public GovernanceOutcome SuspendTenant(Guid id)
    {
        return Mutate(delegate
        {
            if (!_ledger.Tenants.TryGetValue(id, out var tenant))
            {
                return Fail("tenant missing");
            }

            _ledger.Tenants[id] = tenant with { Status = "suspended" };
            return Commit("tenant", id, "suspend");
        });
    }

    public GovernanceOutcome Hold(string category, DateTimeOffset until)
    {
        return Mutate(delegate
        {
            _ledger.Holds[category] = until;
            return new GovernanceOutcome(true, "held");
        });
    }

    public bool MayPurge(string category, DateTimeOffset created, DateTimeOffset now)
    {
        lock (_ledger.Gate)
        {
            if (_ledger.Holds.TryGetValue(category, out var until) && until > now)
            {
                return false;
            }
        }

        return RetentionDefaults.MayPurge(category, created, now, false);
    }

    public GovernanceOutcome ActivateDigest(string digest)
    {
        return Mutate(delegate
        {
            if (digest.Trim().Length == 0)
            {
                return Fail("digest missing");
            }

            _ledger.ActiveDigest = digest.Trim();
            return new GovernanceOutcome(true, "activated");
        });
    }

    public bool DigestMatches(string digest)
    {
        lock (_ledger.Gate)
        {
            return _ledger.ActiveDigest.Length > 0 && _ledger.ActiveDigest == digest;
        }
    }

    public string ActiveDigestText()
    {
        lock (_ledger.Gate)
        {
            return _ledger.ActiveDigest;
        }
    }

    public void NoteCheckpoint(DateTimeOffset anchoredAt, DateTimeOffset now)
    {
        lock (_ledger.Gate)
        {
            _ledger.CheckpointLag = now - anchoredAt;
            GovernanceMeters.SetCheckpointLag(_ledger.CheckpointLag);
        }
    }

    public bool CheckpointAlert
    {
        get
        {
            lock (_ledger.Gate)
            {
                return _ledger.CheckpointLag > TimeSpan.FromMinutes(2);
            }
        }
    }
    public GovernanceOutcome AppendSecurity(string actor, string action, string outcome)
    {
        return Security(actor, action, outcome);
    }

    public bool VerifyPartition(string partition)
    {
        lock (_ledger.Gate)
        {
            return _ledger.Chains.TryGetValue(partition, out var chain) && chain.Intact();
        }
    }

    public GovernanceOutcome Anchor(string partition, IAnchorStore store)
    {
        lock (_ledger.Gate)
        {
            if (!_ledger.Chains.TryGetValue(partition, out var chain))
            {
                return Fail("partition missing");
            }

            var body = chain.Checkpoint();
            if (!store.PutImmutable(partition, body))
            {
                GovernanceMeters.AuditVerificationFailures.Add(1);
                return Fail("anchor refused");
            }

            chain.Anchored = body;
            return new GovernanceOutcome(true, "anchored");
        }
    }

    public GovernanceOutcome EnterRecovery(IReadOnlyCollection<string> revokedOutsideBackup)
    {
        lock (_ledger.Gate)
        {
            _ledger.Lockdown = true;
            GovernanceMeters.SetLockdown(true);
            foreach (var prefix in revokedOutsideBackup)
            {
                _ledger.RevokedPrefixes.Add(prefix);
                if (_ledger.Keys.TryGetValue(prefix, out var key))
                {
                    _ledger.Keys[prefix] = key with { Quarantined = true, RevokedAt = key.RevokedAt ?? DateTimeOffset.UtcNow };
                }
            }

            foreach (var prefix in _ledger.Keys.Keys.ToArray())
            {
                var key = _ledger.Keys[prefix];
                _ledger.Keys[prefix] = key with { Quarantined = true };
            }

            return new GovernanceOutcome(true, "lockdown");
        }
    }

    public bool Lockdown
    {
        get
        {
            lock (_ledger.Gate)
            {
                return _ledger.Lockdown;
            }
        }
    }

    public int SecurityEvents
    {
        get
        {
            lock (_ledger.Gate)
            {
                return _ledger.Security.Count;
            }
        }
    }

    private GovernanceOutcome Mutate(Func<GovernanceOutcome> work)
    {
        lock (_ledger.Gate)
        {
            if (_ledger.Lockdown || _ledger.CheckpointLag > TimeSpan.FromMinutes(5))
            {
                return Fail("recovery lockdown");
            }

            return work();
        }
    }

    private GovernanceOutcome Commit(string resource, Guid id, string action)
    {
        var partition = resource;
        if (!_ledger.Chains.TryGetValue(partition, out var chain))
        {
            chain = new Chain();
            _ledger.Chains[partition] = chain;
        }

        chain.Append(resource + "|" + id.ToString("N") + "|" + action);
        return new GovernanceOutcome(true, "committed");
    }

    private GovernanceOutcome Security(string actor, string action, string outcome)
    {
        lock (_ledger.Gate)
        {
            _ledger.Security.Add(actor + "|" + action + "|" + outcome);
            return new GovernanceOutcome(outcome != "denied", outcome);
        }
    }

    private void PublishCallAge(DateTimeOffset now)
    {
        var oldest = 0d;
        foreach (var call in _ledger.Calls.Values)
        {
            if (call.Status != "pending")
            {
                continue;
            }

            var age = (now - call.IngestedAt).TotalSeconds;
            if (age > oldest)
            {
                oldest = age;
            }
        }

        GovernanceMeters.SetUnattributedAge(oldest);
    }

    private static GovernanceOutcome Fail(string detail)
    {
        return new GovernanceOutcome(false, detail);
    }

    private string Verifier(string secret)
    {
        using (var hmac = new HMACSHA256(_pepper))
        {
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        }
    }

    private sealed class Ledger
    {
        public object Gate { get; } = new();

        public Dictionary<Guid, Org> Organizations { get; } = new();

        public Dictionary<Guid, TenantRow> Tenants { get; } = new();

        public Dictionary<Guid, AppRow> Apps { get; } = new();

        public Dictionary<Guid, string> Identities { get; } = new();

        public HashSet<string> ExternalSubjects { get; } = new();

        public Dictionary<string, KeyRow> Keys { get; } = new();

        public HashSet<string> RevokedPrefixes { get; } = new();

        public HashSet<string> StaffAssignments { get; } = new();

        public HashSet<string> Grants { get; } = new();

        public HashSet<string> BilledEvents { get; } = new();

        public decimal BilledAmount { get; set; }

        public Dictionary<Guid, CallRow> Calls { get; } = new();

        public Dictionary<Guid, string> External { get; } = new();

        public Dictionary<string, Chain> Chains { get; } = new();

        public List<string> Security { get; } = new();

        public int Epoch { get; set; }

        public bool Lockdown { get; set; }

        public TimeSpan CheckpointLag { get; set; }

        public int BillingDuplicates { get; set; }

        public int Reconciliations { get; set; }

        public Dictionary<string, string> Idempotency { get; } = new();

        public Dictionary<Guid, Approval> Approvals { get; } = new();

        public Dictionary<Guid, DateTimeOffset> BreakGlass { get; } = new();

        public Dictionary<string, DateTimeOffset> Holds { get; } = new();

        public string ActiveDigest { get; set; } = string.Empty;
    }

    private sealed record Approval(Guid Requester, DateTimeOffset ExpiresAt, bool Consumed);

    private sealed record Org(Guid Id, string Name, string Status);

    private sealed record TenantRow(Guid Id, Guid OrganizationId, string Name, string Status);

    private sealed record AppRow(Guid Id, Guid OrganizationId, Guid TenantId, string Name, string Status);

    private sealed record KeyRow(Guid AppId, string Hash, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, bool Quarantined);

    private sealed record CallRow(Guid Id, Guid? TenantId, Guid? AppId, string Origin, string Status, DateTimeOffset IngestedAt);

    private sealed class Chain
    {
        private string _previous = string.Empty;

        public string Anchored { get; set; } = string.Empty;

        private readonly List<string> _hashes = new();

        public void Append(string canonical)
        {
            var text = _previous + "|" + (_hashes.Count + 1).ToString() + "|" + canonical;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            _hashes.Add(hash);
            _previous = hash;
        }

        public string Checkpoint()
        {
            return _previous;
        }

        public bool Intact()
        {
            return _hashes.Count > 0 && _previous == _hashes[^1];
        }
    }
}

public interface IAnchorStore
{
    bool PutImmutable(string key, string body);
}

public sealed class MemoryAnchorStore : IAnchorStore
{
    private readonly Dictionary<string, string> _objects = new();

    public bool PutImmutable(string key, string body)
    {
        if (_objects.ContainsKey(key))
        {
            return false;
        }

        _objects[key] = body;
        return true;
    }

    public bool TryReplace(string key, string body)
    {
        if (!_objects.ContainsKey(key))
        {
            return false;
        }

        return false;
    }
}

public static class ObjectLockRules
{
    public static bool CanDeleteVersion(bool compliance, DateTimeOffset retainUntil, DateTimeOffset now, bool adminApi)
    {
        if (!compliance)
        {
            return true;
        }

        if (retainUntil > now)
        {
            return false;
        }

        return !adminApi || retainUntil <= now;
    }

    public static bool CanShortenRetention(bool compliance, DateTimeOffset current, DateTimeOffset proposed)
    {
        if (!compliance)
        {
            return true;
        }

        return proposed >= current;
    }
}

public static class GovernanceSchema
{
    public const string PostgreSql =
        """
        CREATE TABLE IF NOT EXISTS organization (
            id uuid PRIMARY KEY,
            name varchar(200) NOT NULL,
            status varchar(32) NOT NULL,
            version bigint NOT NULL
        );
        CREATE TABLE IF NOT EXISTS tenant (
            id uuid PRIMARY KEY,
            organization_id uuid NOT NULL REFERENCES organization(id),
            name varchar(200) NOT NULL,
            status varchar(32) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS app (
            id uuid PRIMARY KEY,
            organization_id uuid NOT NULL REFERENCES organization(id),
            tenant_id uuid NOT NULL REFERENCES tenant(id),
            name varchar(200) NOT NULL,
            status varchar(32) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS external_identity (
            id uuid PRIMARY KEY,
            identity_id uuid NOT NULL,
            provider_id uuid NOT NULL,
            external_subject varchar(256) NOT NULL,
            UNIQUE (provider_id, external_subject)
        );
        CREATE TABLE IF NOT EXISTS usage_event (
            id uuid PRIMARY KEY,
            organization_id uuid NOT NULL REFERENCES organization(id),
            tenant_id uuid NOT NULL REFERENCES tenant(id),
            app_id uuid NOT NULL REFERENCES app(id),
            provider varchar(64) NOT NULL,
            integration_id uuid NOT NULL,
            source_event_id varchar(256) NOT NULL,
            UNIQUE (provider, integration_id, source_event_id)
        );
        CREATE TABLE IF NOT EXISTS audit_event (
            id uuid PRIMARY KEY,
            organization_id uuid,
            actor_id uuid,
            action varchar(64) NOT NULL,
            created_at timestamptz NOT NULL
        );
        CREATE TABLE IF NOT EXISTS security_event (
            id uuid PRIMARY KEY,
            actor varchar(128) NOT NULL,
            action varchar(64) NOT NULL,
            outcome varchar(32) NOT NULL,
            created_at timestamptz NOT NULL
        );
        CREATE TABLE IF NOT EXISTS inbox_processed (
            event_id uuid NOT NULL,
            consumer_id varchar(128) NOT NULL,
            PRIMARY KEY (event_id, consumer_id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS tenant_owner ON tenant (id, organization_id);
        ALTER TABLE app ADD CONSTRAINT app_same_owner FOREIGN KEY (tenant_id, organization_id) REFERENCES tenant (id, organization_id);
        """;

    public const string Portable =
        """
        CREATE TABLE IF NOT EXISTS organization (
            id char(36) NOT NULL PRIMARY KEY,
            name varchar(200) NOT NULL,
            status varchar(32) NOT NULL,
            version bigint NOT NULL
        );
        CREATE TABLE IF NOT EXISTS tenant (
            id char(36) NOT NULL PRIMARY KEY,
            organization_id char(36) NOT NULL,
            name varchar(200) NOT NULL,
            status varchar(32) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS app (
            id char(36) NOT NULL PRIMARY KEY,
            organization_id char(36) NOT NULL,
            tenant_id char(36) NOT NULL,
            name varchar(200) NOT NULL,
            status varchar(32) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS external_identity (
            id char(36) NOT NULL PRIMARY KEY,
            identity_id char(36) NOT NULL,
            provider_id char(36) NOT NULL,
            external_subject varchar(256) NOT NULL,
            UNIQUE (provider_id, external_subject)
        );
        CREATE TABLE IF NOT EXISTS audit_event (
            id char(36) NOT NULL PRIMARY KEY,
            actor_id char(36) NOT NULL,
            action varchar(64) NOT NULL,
            created_at varchar(40) NOT NULL
        );
        """;
}

public static class RetentionDefaults
{
    public static bool MayPurge(string category, DateTimeOffset created, DateTimeOffset now, bool legalHold)
    {
        if (legalHold)
        {
            return false;
        }

        if (category == "security-audit")
        {
            return created.AddMonths(13) <= now;
        }

        if (category == "anchor" || category == "invoice")
        {
            return created.AddYears(5) <= now;
        }

        if (category == "call" || category == "closed-account")
        {
            return created.AddDays(90) <= now;
        }

        if (category == "telemetry" || category == "audio" || category == "dedup")
        {
            return created.AddDays(30) <= now;
        }

        return false;
    }
}

public static class PiiMask
{
    public static string Email(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        return email.Substring(0, 1) + "***" + email.Substring(at);
    }
}

public static class GovernanceProof
{
    public const bool WormApproved = false;

    public const bool SloMeasured = false;

    public const bool RestoreDrillPassed = false;
}
