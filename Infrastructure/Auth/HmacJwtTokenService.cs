using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Auth;
using Application.Interfaces;
using Application.Logging;
using Domain.Users;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Auth;

// Minimal hand-rolled JWT (HS256). Claims: sub (userId), email, iat, exp.
public sealed class HmacJwtTokenService : ITokenService
{
    private static readonly string EncodedHeader =
        Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));

    private readonly byte[] _key;
    private readonly TimeSpan _lifetime;
    private readonly ILogger<HmacJwtTokenService> _logger;

    public HmacJwtTokenService(byte[] key, TimeSpan lifetime, ILogger<HmacJwtTokenService> logger)
    {
        if (key.Length < 32)
            throw new ArgumentException("JWT signing key must be at least 32 bytes.", nameof(key));

        _key = key;
        _lifetime = lifetime;
        _logger = logger;
    }

    public AuthToken Create(UserId userId, Email email)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.Add(_lifetime);

        JwtPayload payload = new(
            userId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            email.EmailAddress,
            now.ToUnixTimeSeconds(),
            expiresAt.ToUnixTimeSeconds());

        string encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        string signingInput = EncodedHeader + "." + encodedPayload;
        string signature = Base64UrlEncode(Sign(signingInput));

        return new AuthToken(signingInput + "." + signature, expiresAt.UtcDateTime);
    }

    public TokenClaims? Validate(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != EncodedHeader)
        {
            _logger.Info("invalid token: malformed or unexpected header");
            return null;
        }

        byte[]? signature = Base64UrlDecode(parts[2]);
        if (signature is null)
        {
            _logger.Info("invalid token: signature not base64url");
            return null;
        }

        byte[] expected = Sign(parts[0] + "." + parts[1]);
        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
        {
            _logger.Info("invalid token: bad signature");
            return null;
        }

        byte[]? payloadBytes = Base64UrlDecode(parts[1]);
        if (payloadBytes is null)
        {
            _logger.Info("invalid token: payload not base64url");
            return null;
        }

        JwtPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<JwtPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            _logger.Info("invalid token: payload not valid JSON");
            return null;
        }

        if (payload is null || payload.exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            _logger.Info("invalid token: empty payload or expired");
            return null;
        }

        if (!int.TryParse(payload.sub, out int userId) || UserId.TryCreate(userId) is null)
        {
            _logger.Info("invalid token: bad sub claim");
            return null;
        }

        return new TokenClaims(userId, payload.email);
    }

    private byte[] Sign(string input) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(input));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Base64UrlDecode(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Lower-case names are the registered JWT claim names.
    private sealed record JwtPayload(string sub, string email, long iat, long exp);
}
