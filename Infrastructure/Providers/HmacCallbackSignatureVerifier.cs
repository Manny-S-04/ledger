using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;
using Application.Providers;

namespace Infrastructure.Providers;

// The provider sends two headers with every callback:
//   X-Provider-Timestamp: unix seconds when it sent the callback
//   X-Provider-Signature: hex HMAC-SHA256(secret, "<timestamp>." + raw body)
// A plain hash would prove nothing (anyone can hash the body); the HMAC needs the shared secret.
// The timestamp is inside the signature, so it can't be changed to make an old callback look fresh.
public sealed class HmacCallbackSignatureVerifier : ICallbackSignatureVerifier
{
    public const string TimestampHeader = "X-Provider-Timestamp";
    public const string SignatureHeader = "X-Provider-Signature";

    private readonly byte[] _secret;
    private readonly TimeSpan _tolerance;
    private readonly TimeProvider _time;

    public HmacCallbackSignatureVerifier(byte[] secret, TimeSpan tolerance, TimeProvider time)
    {
        if (secret.Length < 32)
            throw new ArgumentException("Callback secret must be at least 32 bytes.", nameof(secret));

        _secret = secret;
        _tolerance = tolerance;
        _time = time;
    }

    public CallbackSignatureResult Verify(string? timestamp, string? signature, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
            return CallbackSignatureResult.Missing;

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            return CallbackSignatureResult.Malformed;

        byte[] given;
        try
        {
            given = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return CallbackSignatureResult.Malformed;
        }

        byte[] expected = Sign(_secret, timestamp, body);
        if (!CryptographicOperations.FixedTimeEquals(expected, given))
            return CallbackSignatureResult.Mismatch;

        TimeSpan age = _time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age.Duration() > _tolerance)
            return CallbackSignatureResult.Stale;

        return CallbackSignatureResult.Valid;
    }

    // What the provider computes. Public so tests (and later FakeProvider's copy) sign the same way.
    public static byte[] Sign(byte[] secret, string timestamp, ReadOnlySpan<byte> body)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        byte[] message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(prefix.Length));
        return HMACSHA256.HashData(secret, message);
    }
}
