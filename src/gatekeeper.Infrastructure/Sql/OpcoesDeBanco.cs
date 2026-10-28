namespace gatekeeper.Persistence.Adapters;

/// <summary>
/// Provedor invariante do ADO.NET mais a connection string. Quem leu o appsettings foi o host.
/// </summary>
public sealed class OpcoesDeBanco
{
    public OpcoesDeBanco(string provedorInvariante, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(provedorInvariante))
        {
            throw new ArgumentException("Provedor do banco e obrigatorio.", nameof(provedorInvariante));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string e obrigatoria.", nameof(connectionString));
        }

        ProvedorInvariante = provedorInvariante;
        ConnectionString = connectionString;
    }

    public string ProvedorInvariante { get; }

    public string ConnectionString { get; }
}
