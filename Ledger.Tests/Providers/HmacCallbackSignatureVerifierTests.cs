using System.Text;
using Application.Providers;
using Infrastructure.Providers;

namespace Ledger.Tests.Providers;

public class HmacCallbackSignatureVerifierTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("test-provider-callback-secret-0123456789abcdef");
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"reference\":\"42\",\"status\":\"Succeeded\"}");

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static HmacCallbackSignatureVerifier Verifier() =>
        new HmacCallbackSignatureVerifier(Secret, TimeSpan.FromMinutes(5), new FixedTime(Now));

    private static string Timestamp(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string SignHex(byte[] secret, string timestamp, byte[] body) =>
        Convert.ToHexString(HmacCallbackSignatureVerifier.Sign(secret, timestamp, body));

    [Fact]
    public void Verify_CorrectlySignedFreshCallback_ReturnsValid()
    {
        string ts = Timestamp(Now);

        Assert.Equal(CallbackSignatureResult.Valid, Verifier().Verify(ts, SignHex(Secret, ts, Body), Body));
    }

    [Fact]
    public void Verify_LowercaseHexSignature_ReturnsValid()
    {
        string ts = Timestamp(Now);

        Assert.Equal(CallbackSignatureResult.Valid, Verifier().Verify(ts, SignHex(Secret, ts, Body).ToLowerInvariant(), Body));
    }

    [Fact]
    public void Verify_BodyChangedAfterSigning_ReturnsMismatch()
    {
        string ts = Timestamp(Now);
        string signature = SignHex(Secret, ts, Body);
        byte[] tampered = Encoding.UTF8.GetBytes("{\"reference\":\"43\",\"status\":\"Succeeded\"}");

        Assert.Equal(CallbackSignatureResult.Mismatch, Verifier().Verify(ts, signature, tampered));
    }

    [Fact]
    public void Verify_SignedWithAnotherSecret_ReturnsMismatch()
    {
        string ts = Timestamp(Now);
        byte[] otherSecret = Encoding.UTF8.GetBytes("someone-elses-secret-0123456789abcdefghijkl");

        Assert.Equal(CallbackSignatureResult.Mismatch, Verifier().Verify(ts, SignHex(otherSecret, ts, Body), Body));
    }

    [Fact]
    public void Verify_TimestampChangedToLookFresh_ReturnsMismatch()
    {
        string oldTs = Timestamp(Now.AddHours(-1));
        string signature = SignHex(Secret, oldTs, Body);

        Assert.Equal(CallbackSignatureResult.Mismatch, Verifier().Verify(Timestamp(Now), signature, Body));
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(6)]
    public void Verify_TimestampOutsideTolerance_ReturnsStale(int minutes)
    {
        string ts = Timestamp(Now.AddMinutes(minutes));

        Assert.Equal(CallbackSignatureResult.Stale, Verifier().Verify(ts, SignHex(Secret, ts, Body), Body));
    }

    [Theory]
    [InlineData(null, "ab")]
    [InlineData("1761307200", null)]
    [InlineData("", "ab")]
    public void Verify_MissingHeader_ReturnsMissing(string? timestamp, string? signature) =>
        Assert.Equal(CallbackSignatureResult.Missing, Verifier().Verify(timestamp, signature, Body));

    [Theory]
    [InlineData("not-a-number", "ab")]
    [InlineData("-5", "ab")]
    [InlineData("99999999999999999", "ab")]
    [InlineData("1761307200", "not-hex")]
    public void Verify_MalformedHeader_ReturnsMalformed(string timestamp, string signature) =>
        Assert.Equal(CallbackSignatureResult.Malformed, Verifier().Verify(timestamp, signature, Body));

    [Fact]
    public void Constructor_ShortSecret_Throws() =>
        Assert.Throws<ArgumentException>(() => new HmacCallbackSignatureVerifier(new byte[31], TimeSpan.FromMinutes(5), TimeProvider.System));
}
