namespace gatekeeper.Authorization;

public sealed record PolicyExportRule(string Role, string Action, string Effect);

public sealed record PolicyExport(string Resource, IReadOnlyList<PolicyExportRule> Rules);

public sealed record PolicyBatch(IReadOnlyList<PolicyExport> Active, IReadOnlyList<string> DisabledResources);

public static class PolicyExportBuilder
{
    public static PolicyBatch Build(IReadOnlyList<AccessRule> rules)
    {
        var resources = new List<string>();
        foreach (var rule in rules)
        {
            if (!resources.Contains(rule.Resource))
            {
                resources.Add(rule.Resource);
            }
        }

        var active = new List<PolicyExport>();
        var disabled = new List<string>();
        foreach (var resource in resources)
        {
            var rulesForResource = new List<PolicyExportRule>();
            foreach (var rule in rules)
            {
                if (rule.Resource == resource && rule.RemovedOn is null)
                {
                    rulesForResource.Add(new PolicyExportRule(rule.Role, rule.Action, rule.Effect));
                }
            }

            if (rulesForResource.Count == 0)
            {
                disabled.Add(resource);
            }
            else
            {
                active.Add(new PolicyExport(resource, rulesForResource));
            }
        }

        return new PolicyBatch(active, disabled);
    }
}
