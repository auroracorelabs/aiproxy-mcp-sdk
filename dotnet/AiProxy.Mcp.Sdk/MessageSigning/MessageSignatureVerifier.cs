using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiProxy.Mcp.Sdk.Internal;

namespace AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>Verified identity carried by a <c>message_signed</c> request.</summary>
public sealed record MessageSignatureIdentity(
    string? UserId,
    string? Username,
    string? Name,
    string? Email,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Roles,
    string RequestId,
    string KeyId)
{
    public const string AuthenticationType = "message_signed";
    public const string ClaimSubject = "sub";
    public const string ClaimUsername = "username";
    public const string ClaimName = "name";
    public const string ClaimEmail = "email";
    public const string ClaimGroup = "group";
    public const string ClaimRole = "role";
    public const string ClaimRequestId = "jti";
    public const string ClaimKeyId = "kid";

    public ClaimsPrincipal ToPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimRequestId, RequestId),
            new(ClaimKeyId, KeyId),
        };
        if (UserId is not null)
            claims.Add(new Claim(ClaimSubject, UserId));
        if (Username is not null)
            claims.Add(new Claim(ClaimUsername, Username));
        if (Name is not null)
            claims.Add(new Claim(ClaimName, Name));
        if (Email is not null)
            claims.Add(new Claim(ClaimEmail, Email));
        claims.AddRange(Groups.Select(g => new Claim(ClaimGroup, g)));
        claims.AddRange(Roles.Select(r => new Claim(ClaimRole, r)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, ClaimName, ClaimRole));
    }
}

public readonly record struct MessageSignatureResult(MessageSignatureIdentity? Identity, string? Error)
{
    public bool Success => Identity is not null;

    public static MessageSignatureResult Fail(string error) => new(null, error);
}

/// <summary>
/// Verifies the compact JWS in <c>Mcp-Message-Signature</c> (ES256 only). Checks, in order: format,
/// <c>alg</c>, <c>kid</c>, signature, <c>v</c>, <c>aud</c>, time window, <c>htm</c>/<c>htp</c>, body
/// hash, single-use <c>jti</c>. The <c>jti</c> is recorded only once everything else passed.
/// </summary>
public sealed class MessageSignatureVerifier
{
    public const string HeaderName = "Mcp-Message-Signature";
    public const string Algorithm = "ES256";
    public const int PayloadVersion = 1;
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(120);

    private const int MaxTokenLength = 16 * 1024;

    private readonly MessageSignatureOptions _options;
    private readonly JtiReplayCache _replayCache;
    private readonly TimeProvider _time;

    public MessageSignatureVerifier(MessageSignatureOptions options, JtiReplayCache replayCache, TimeProvider? time = null)
    {
        _options = Guard.NotNull(options, nameof(options));
        _replayCache = Guard.NotNull(replayCache, nameof(replayCache));
        _time = time ?? TimeProvider.System;
    }

    public MessageSignatureResult Verify(string? token, string method, string pathAndQuery, ReadOnlySpan<byte> body)
    {
        if (token is null || string.IsNullOrWhiteSpace(token))
            return MessageSignatureResult.Fail("missing signature");
        token = token.Trim();
        if (token.Length > MaxTokenLength)
            return MessageSignatureResult.Fail("signature too large");

        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
            return MessageSignatureResult.Fail("malformed signature");

        if (!TryDecode(parts[0], out var headerBytes) || !TryDecode(parts[1], out var payloadBytes)
            || !TryDecode(parts[2], out var signature))
            return MessageSignatureResult.Fail("malformed signature encoding");

        string? kid;
        try
        {
            using var header = JsonDocument.Parse(headerBytes);
            if (header.RootElement.ValueKind != JsonValueKind.Object
                || GetString(header.RootElement, "alg") != Algorithm)
                return MessageSignatureResult.Fail("unsupported alg");
            kid = GetString(header.RootElement, "kid");
        }
        catch (JsonException)
        {
            return MessageSignatureResult.Fail("malformed header");
        }

        if (kid is null || kid.Length == 0 || !_options.TrustedKeys.TryGetValue(kid, out var key))
            return MessageSignatureResult.Fail("unknown kid");

        var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        if (signature.Length != 64
            // VerifyData without a format argument expects IEEE P1363 (r||s) on every runtime.
            || !key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256))
            return MessageSignatureResult.Fail("bad signature");

        try
        {
            using var payloadDoc = JsonDocument.Parse(payloadBytes);
            var p = payloadDoc.RootElement;
            if (p.ValueKind != JsonValueKind.Object)
                return MessageSignatureResult.Fail("malformed payload");

            if (!p.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != PayloadVersion)
                return MessageSignatureResult.Fail("unsupported payload version");

            if (!string.Equals(GetString(p, "aud"), _options.Audience, StringComparison.Ordinal))
                return MessageSignatureResult.Fail("audience mismatch");

            var iss = GetString(p, "iss");
            if (iss is null || iss.Length == 0)
                return MessageSignatureResult.Fail("missing iss");
            if (_options.ExpectedIssuer is not null
                && !string.Equals(iss, _options.ExpectedIssuer, StringComparison.Ordinal))
                return MessageSignatureResult.Fail("issuer mismatch");

            if (!TryGetLong(p, "iat", out var iat) || !TryGetLong(p, "exp", out var exp))
                return MessageSignatureResult.Fail("missing iat/exp");
            var now = _time.GetUtcNow().ToUnixTimeSeconds();
            var skew = (long)ClockSkew.TotalSeconds;
            if (exp <= iat || exp - iat > (long)MaxLifetime.TotalSeconds)
                return MessageSignatureResult.Fail("invalid lifetime");
            if (iat > now + skew)
                return MessageSignatureResult.Fail("issued in the future");
            if (exp < now - skew)
                return MessageSignatureResult.Fail("expired");

            if (!string.Equals(GetString(p, "htm"), method, StringComparison.OrdinalIgnoreCase))
                return MessageSignatureResult.Fail("method mismatch");
            if (!string.Equals(GetString(p, "htp"), pathAndQuery, StringComparison.Ordinal))
                return MessageSignatureResult.Fail("path mismatch");

            var expectedBodyHash = Base64UrlCodec.Encode(Sha256.Hash(body.ToArray()));
            if (!ConstantTime.Equals(
                    Encoding.ASCII.GetBytes(GetString(p, "bh") ?? ""),
                    Encoding.ASCII.GetBytes(expectedBodyHash)))
                return MessageSignatureResult.Fail("body hash mismatch");

            var jti = GetString(p, "jti");
            if (jti is null || jti.Length == 0 || jti.Length > 128)
                return MessageSignatureResult.Fail("missing jti");

            var identity = new MessageSignatureIdentity(
                NullIfEmpty(GetString(p, "sub")),
                NullIfEmpty(GetString(p, "username")),
                NullIfEmpty(GetString(p, "name")),
                NullIfEmpty(GetString(p, "email")),
                GetStringArray(p, "groups"),
                GetStringArray(p, "roles"),
                jti,
                kid);

            if (!_replayCache.TryRegister(jti, exp + skew, now))
                return MessageSignatureResult.Fail("replayed jti");

            return new MessageSignatureResult(identity, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return MessageSignatureResult.Fail("malformed payload");
        }
    }

    private static bool TryDecode(string segment, out byte[] bytes) => Base64UrlCodec.TryDecode(segment, out bytes);

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetLong(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value);
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Array)
            return [];
        return e.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(x.GetString()))
            .Select(x => x.GetString()!)
            .ToList();
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
