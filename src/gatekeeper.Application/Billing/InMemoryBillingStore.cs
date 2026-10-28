namespace gatekeeper.Billing;

public interface IBillingStore
{
    IReadOnlyList<Organization> Organizations();

    void Save(Organization organization);

    void RemoveOrganization(Guid id);
}

public sealed class InMemoryBillingStore : IBillingStore
{
    private readonly State _state = new();

    public IReadOnlyList<Organization> Organizations()
    {
        lock (_state)
        {
            return Active(_state.Organizations).ToArray();
        }
    }

    public void Save(Organization organization)
    {
        lock (_state)
        {
            Replace(_state.Organizations, organization, item => item.Id == organization.Id);
        }
    }

    public void RemoveOrganization(Guid id)
    {
        lock (_state)
        {
            Deactivate(_state.Organizations, item => item.Id == id, item => item with { RemovedOn = DateTimeOffset.UtcNow });
        }
    }

    private static IEnumerable<T> Active<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            if (item is Organization organization && organization.RemovedOn is null)
            {
                yield return item;
            }
        }
    }

    private static void Deactivate<T>(List<T> list, Predicate<T> match, Func<T, T> mark)
    {
        var index = list.FindIndex(match);
        if (index < 0)
        {
            return;
        }

        list[index] = mark(list[index]);
    }

    private static void Replace<T>(List<T> list, T item, Predicate<T> match)
    {
        var index = list.FindIndex(match);
        if (index < 0)
        {
            list.Add(item);
            return;
        }

        list[index] = item;
    }

    private sealed class State
    {
        public List<Organization> Organizations { get; } = new();
    }
}
