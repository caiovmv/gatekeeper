namespace gatekeeper.Authorization;

public sealed class InMemoryPolicyStore : IPolicyStore
{
    private readonly List<AccessRule> _rules = new();

    public InMemoryPolicyStore()
    {
        Grant(new[] { "Staff.Admin", "admin" }, new[] { "plan", "subscription", "organization", "member", "machine-key", "team", "team-member", "policy" }, new[] { "create", "update", "delete", "read" });
        Grant(new[] { "Staff.Admin", "admin", "Staff.Support", "support" }, new[] { "organization", "member", "team", "team-member" }, new[] { "read" });
        Grant(new[] { "Staff.Admin", "admin", "Staff.Support", "support", "Staff.Auditor", "auditor" }, new[] { "audit", "access-log" }, new[] { "read" });
        Grant(new[] { "Staff.Admin", "admin" }, new[] { "audit", "access-log" }, new[] { "ingest" });
    }

    public IReadOnlyList<AccessRule> Rules()
    {
        lock (_rules)
        {
            return _rules.Where(rule => rule.RemovedOn is null).ToArray();
        }
    }

    public IReadOnlyList<AccessRule> History()
    {
        lock (_rules)
        {
            return _rules.ToArray();
        }
    }

    public void Save(AccessRule rule)
    {
        lock (_rules)
        {
            var index = _rules.FindIndex(item => item.Id == rule.Id);
            if (index < 0)
            {
                _rules.Add(rule);
                return;
            }

            _rules[index] = rule;
        }
    }

    public void Remove(Guid id, DateTimeOffset now)
    {
        lock (_rules)
        {
            var index = _rules.FindIndex(item => item.Id == id && item.RemovedOn is null);
            if (index < 0)
            {
                return;
            }

            var current = _rules[index];
            _rules[index] = current with { RemovedOn = now };
        }
    }

    public bool Allows(IReadOnlyList<string> roles, string action, string resource)
    {
        var allowed = false;
        lock (_rules)
        {
            foreach (var rule in _rules)
            {
                if (rule.RemovedOn is not null || rule.Resource != resource || rule.Action != action || !roles.Contains(rule.Role))
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
        }

        return allowed;
    }

    private void Grant(string[] roles, string[] resources, string[] actions)
    {
        foreach (var role in roles)
        {
            foreach (var resource in resources)
            {
                foreach (var action in actions)
                {
                    if (_rules.Any(rule => rule.Role == role && rule.Resource == resource && rule.Action == action))
                    {
                        continue;
                    }

                    _rules.Add(new AccessRule(Guid.NewGuid(), resource, role, action, "allow", null));
                }
            }
        }
    }
}
