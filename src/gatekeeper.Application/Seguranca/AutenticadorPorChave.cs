using System.Security.Cryptography;
using System.Text;

namespace gatekeeper.Identity;

public interface IAutenticador
{
    bool Confere(string recebida, string esperada);
}

public sealed class AutenticadorPorChave : IAutenticador
{
    public bool Confere(string recebida, string esperada)
    {
        var a = Encoding.UTF8.GetBytes(recebida ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(esperada ?? string.Empty);
        if (a.Length != b.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
