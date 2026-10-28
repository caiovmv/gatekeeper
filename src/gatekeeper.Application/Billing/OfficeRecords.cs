namespace gatekeeper.Billing;

public interface IMachineKeyGate
{
    bool Touch(string presented, DateTimeOffset now);
}
