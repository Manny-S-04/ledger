using System.Security.Cryptography;
using System.Text;

namespace FakeProvider.Delivery;

// Same scheme the ledger verifies:
//   X-Provider-Timestamp: unix seconds
//   X-Provider-Signature: hex HMAC-SHA256(secret, "<timestamp>." + raw body)
public static class CallbackSigner
{
    public const string TimestampHeader = "X-Provider-Timestamp";
    public const string SignatureHeader = "X-Provider-Signature";

    public static string Sign(byte[] secret, string timestamp, byte[] body)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        byte[] message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message, prefix.Length);
        return Convert.ToHexString(HMACSHA256.HashData(secret, message)).ToLowerInvariant();
    }
}
