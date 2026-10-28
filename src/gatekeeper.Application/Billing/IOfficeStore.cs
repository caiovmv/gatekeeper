namespace gatekeeper.Billing;

public interface IOfficeStore
{
    IReadOnlyList<Member> Members();

    void Save(Member member);

    void RemoveMember(Guid id);

    IReadOnlyList<MachineKey> MachineKeys();

    IssuedMachineKey Issue(Guid id, Guid organizationId, string name, DateTimeOffset now);

    void Revoke(Guid id, DateTimeOffset now);

    void Rename(Guid id, string name);
}
