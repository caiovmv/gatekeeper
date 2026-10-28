using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace gatekeeper.Identity;

public static class OneTimePassword
{
    public static string Secret()
    {
        return Base64Url.Encode(RandomNumberGenerator.GetBytes(20));
    }

    public static bool Matches(string secret, string code, DateTimeOffset now)
    {
        if (code.Trim().Length != 6)
        {
            return false;
        }

        var key = Base64Url.Decode(secret);
        if (key is null)
        {
            return false;
        }

        var step = now.ToUnixTimeSeconds() / 30;
        return Timing(key, step) == code.Trim() || Timing(key, step - 1) == code.Trim() || Timing(key, step + 1) == code.Trim();
    }

    private static string Timing(byte[] key, long step)
    {
        var counter = new byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counter[index] = (byte)(step & 0xff);
            step >>= 8;
        }

        var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
        hmac.AppendData(counter);
        var hash = hmac.GetHashAndReset();
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1000000).ToString("D6");
    }
}

public static class PasskeyCeremony
{
    public static bool TryRegister(string origin, string challenge, string credential, out string credentialId, out string publicKey)
    {
        credentialId = string.Empty;
        publicKey = string.Empty;
        if (!TryPayload(credential, out var response))
        {
            return false;
        }

        if (!response.TryGetProperty("attestationObject", out var attestationProperty))
        {
            return false;
        }

        var attestation = Base64Url.Decode(attestationProperty.GetString() ?? string.Empty);
        var client = Client(response);
        if (attestation is null || client is null || !ClientMatches(client, origin, challenge, "webauthn.create"))
        {
            return false;
        }

        if (!TryKey(attestation, Host(origin), out var id, out var x, out var y))
        {
            return false;
        }

        credentialId = Base64Url.Encode(id);
        publicKey = Base64Url.Encode(x) + "." + Base64Url.Encode(y);
        return true;
    }

    public static bool Verify(string origin, string challenge, string publicKey, string credential)
    {
        if (!TryPayload(credential, out var response))
        {
            return false;
        }

        var client = Client(response);
        var authenticator = Property(response, "authenticatorData");
        var signature = Property(response, "signature");
        var parts = publicKey.Split('.');
        if (client is null || authenticator is null || signature is null || parts.Length != 2 || !ClientMatches(client, origin, challenge, "webauthn.get"))
        {
            return false;
        }

        var x = Base64Url.Decode(parts[0]);
        var y = Base64Url.Decode(parts[1]);
        if (x is null || y is null)
        {
            return false;
        }

        var hash = SHA256.HashData(client);
        var signed = new byte[authenticator.Length + hash.Length];
        Buffer.BlockCopy(authenticator, 0, signed, 0, authenticator.Length);
        Buffer.BlockCopy(hash, 0, signed, authenticator.Length, hash.Length);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
        });
        return ecdsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static bool TryPayload(string credential, out JsonElement response)
    {
        response = default;
        try
        {
            using var document = JsonDocument.Parse(credential);
            if (!document.RootElement.TryGetProperty("response", out var value))
            {
                return false;
            }

            response = value.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static byte[]? Client(JsonElement response)
    {
        return Property(response, "clientDataJSON");
    }

    private static byte[]? Property(JsonElement response, string name)
    {
        if (!response.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Base64Url.Decode(value.GetString() ?? string.Empty);
    }

    private static bool ClientMatches(byte[] client, string origin, string challenge, string type)
    {
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(client));
            var root = document.RootElement;
            return root.TryGetProperty("type", out var kind)
                && kind.GetString() == type
                && root.TryGetProperty("origin", out var seenOrigin)
                && seenOrigin.GetString() == origin
                && root.TryGetProperty("challenge", out var seenChallenge)
                && seenChallenge.GetString() == challenge;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryKey(byte[] attestation, string rpId, out byte[] credentialId, out byte[] x, out byte[] y)
    {
        credentialId = Array.Empty<byte>();
        x = Array.Empty<byte>();
        y = Array.Empty<byte>();
        var auth = AuthData(attestation);
        if (auth is null || auth.Length < 37)
        {
            return false;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(rpId));
        if (!auth.AsSpan(0, 32).SequenceEqual(expected) || (auth[32] & 0x40) == 0)
        {
            return false;
        }

        var length = (auth[53] << 8) | auth[54];
        if (auth.Length < 55 + length)
        {
            return false;
        }

        credentialId = auth.AsSpan(55, length).ToArray();
        return Cose(auth.AsSpan(55 + length).ToArray(), out x, out y);
    }

    private static byte[]? AuthData(byte[] attestation)
    {
        var reader = new CborCursor(attestation);
        if (!reader.Map())
        {
            return null;
        }

        byte[]? auth = null;
        while (reader.More())
        {
            var key = reader.Text();
            if (key == "authData")
            {
                auth = reader.Bytes();
            }
            else
            {
                reader.Skip();
            }
        }

        return auth;
    }

    private static bool Cose(byte[] cose, out byte[] x, out byte[] y)
    {
        x = Array.Empty<byte>();
        y = Array.Empty<byte>();
        var reader = new CborCursor(cose);
        if (!reader.Map())
        {
            return false;
        }

        while (reader.More())
        {
            var key = reader.Integer();
            if (key == -2)
            {
                x = reader.Bytes();
            }
            else if (key == -3)
            {
                y = reader.Bytes();
            }
            else
            {
                reader.Skip();
            }
        }

        return x.Length == 32 && y.Length == 32;
    }

    private sealed class CborCursor
    {
        private readonly byte[] _data;
        private int _offset;
        private int _pairs;

        public CborCursor(byte[] data)
        {
            _data = data;
        }

        public bool Map()
        {
            if (Major() != 5)
            {
                return false;
            }

            _pairs = Length();
            return true;
        }

        public bool More()
        {
            return _pairs > 0;
        }

        public string Text()
        {
            _pairs--;
            return Encoding.UTF8.GetString(Payload(3));
        }

        public byte[] Bytes()
        {
            return Payload(2);
        }

        public int Integer()
        {
            _pairs--;
            var major = Major();
            var length = Length();
            if (major == 1)
            {
                return -1 - length;
            }

            return length;
        }

        public void Skip()
        {
            var major = Major();
            if (major == 2 || major == 3)
            {
                var length = Length();
                _offset += length;
                return;
            }

            if (major == 0 || major == 1)
            {
                Length();
                return;
            }

            if (major == 5)
            {
                var count = Length();
                for (var index = 0; index < count; index++)
                {
                    Skip();
                    Skip();
                }
            }
        }

        private int Major()
        {
            return _data[_offset] >> 5;
        }

        private int Length()
        {
            var info = _data[_offset] & 0x1f;
            _offset++;
            if (info < 24)
            {
                return info;
            }

            if (info == 24)
            {
                var value = _data[_offset];
                _offset++;
                return value;
            }

            var wide = (_data[_offset] << 8) | _data[_offset + 1];
            _offset += 2;
            return wide;
        }

        private byte[] Payload(int major)
        {
            if (Major() != major)
            {
                return Array.Empty<byte>();
            }

            var length = Length();
            var value = new byte[length];
            Buffer.BlockCopy(_data, _offset, value, 0, length);
            _offset += length;
            return value;
        }
    }

    private static string Host(string origin)
    {
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return "localhost";
    }
}
