namespace gatekeeper.Audit;

public interface IAccessLogStore
{
    void Append(AccessLog entry);

    IReadOnlyList<AccessLog> Entries { get; }
}

public interface IAccessLogPublisher
{
    void Publish(AccessLog entry);
}

public sealed class PublicadorDeLogNulo : IAccessLogPublisher
{
    public void Publish(AccessLog entry)
    {
    }
}

public sealed class InMemoryAccessLogStore : IAccessLogStore
{
    private readonly List<AccessLog> _entries = new();
    private readonly IAccessLogPublisher _publisher;

    public InMemoryAccessLogStore(IAccessLogPublisher publisher)
    {
        _publisher = publisher;
    }

    public IReadOnlyList<AccessLog> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Append(AccessLog entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }

        _publisher.Publish(entry);
    }
}
