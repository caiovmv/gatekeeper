using System.Security.Cryptography;
using System.Text;
using gatekeeper.Identity;

namespace gatekeeper.Billing;

public sealed class InMemoryOfficeStore : IOfficeStore, IMachineKeyGate
{
    private readonly State _state = new();

    public IReadOnlyList<Member> Members()
    {
        lock (_state)
        {
            return _state.Members.Where(item => item.RemovedOn is null).ToArray();
        }
    }

    public void Save(Member member)
    {
        lock (_state)
        {
            Replace(_state.Members, member, item => item.Id == member.Id);
        }
    }

    public void RemoveMember(Guid id)
    {
        lock (_state)
        {
            var index = _state.Members.FindIndex(item => item.Id == id && item.RemovedOn is null);
            if (index < 0)
            {
                return;
            }

            var current = _state.Members[index];
            _state.Members[index] = current with { RemovedOn = DateTimeOffset.UtcNow };
        }
    }

    public IReadOnlyList<MachineKey> MachineKeys()
    {
        lock (_state)
        {
            return _state.Keys.Select(View).ToArray();
        }
    }

    public IssuedMachineKey Issue(Guid id, Guid organizationId, string name, DateTimeOffset now)
    {
        var secret = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        var stored = new StoredKey(id, organizationId, name.Trim(), secret.Substring(0, 8), Hash(secret), now, null, null);
        lock (_state)
        {
            _state.Keys.Add(stored);
        }

        return new IssuedMachineKey(id, stored.Prefix, secret);
    }

    public void Rename(Guid id, string name)
    {
        lock (_state)
        {
            var index = _state.Keys.FindIndex(item => item.Id == id && item.RevokedOn is null);
            if (index < 0)
            {
                return;
            }

            var current = _state.Keys[index];
            _state.Keys[index] = current with { Name = name.Trim() };
        }
    }

    public void Revoke(Guid id, DateTimeOffset now)
    {
        lock (_state)
        {
            var index = _state.Keys.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return;
            }

            var current = _state.Keys[index];
            _state.Keys[index] = current with { RevokedOn = now };
        }
    }

    public bool Touch(string presented, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var hash = Hash(presented.Trim());
        lock (_state)
        {
            var index = _state.Keys.FindIndex(item => item.RevokedOn is null && Fixed(item.Hash, hash));
            if (index < 0)
            {
                return false;
            }

            var current = _state.Keys[index];
            _state.Keys[index] = current with { LastUsedOn = now };
            return true;
        }
    }

    private static MachineKey View(StoredKey key)
    {
        return new MachineKey(key.Id, key.OrganizationId, key.Name, key.Prefix, key.CreatedOn, key.RevokedOn, key.LastUsedOn);
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
        public List<Member> Members { get; } = new();

        public List<StoredKey> Keys { get; } = new();
    }

    private sealed record StoredKey(Guid Id, Guid OrganizationId, string Name, string Prefix, string Hash, DateTimeOffset CreatedOn, DateTimeOffset? RevokedOn, DateTimeOffset? LastUsedOn);
}
