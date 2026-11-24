using System.Security.Cryptography;
using AiProxy.Mcp.Sdk.Internal;

namespace AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>How the host treats the <c>Mcp-Message-Signature</c> header (instance key <c>MessageSignature</c>).</summary>
public enum MessageSignatureMode
{
    /// <summary>Legacy: no verification, no <c>transportAuth</c> in the manifest.</summary>
    Off,

    /// <summary>Migration: unsigned requests pass (logged); present-but-invalid signatures are rejected.</summary>
    Optional,

    /// <summary>Every request must carry a valid signature.</summary>
    Required,
}

/// <summary>
/// Resolved <c>message_signed</c> settings for one instance. See
/// <c>docs/MESSAGE_SIGNED.md</c> in the SDK repository.
/// </summary>
public sealed class MessageSignatureOptions
{
    public const string DefaultTrustedSignersPath = "/etc/mcp/trusted-signers";
    public const string ModeDirective = "MessageSignature";
    public const string AudienceDirective = "MessageSignatureAudience";
    public const string IssuerDirective = "MessageSignatureIssuer";
    public const string TrustedSignersPathDirective = "TrustedSignersPath";

    public static MessageSignatureOptions Disabled { get; } = new(MessageSignatureMode.Off, "", new Dictionary<string, ECDsa>());

    public MessageSignatureOptions(
        MessageSignatureMode mode,
        string audience,
        IReadOnlyDictionary<string, ECDsa> trustedKeys,
        string? expectedIssuer = null)
    {
        Mode = mode;
        Audience = Guard.NotNull(audience, nameof(audience));
        TrustedKeys = Guard.NotNull(trustedKeys, nameof(trustedKeys));
        ExpectedIssuer = string.IsNullOrWhiteSpace(expectedIssuer) ? null : expectedIssuer!.Trim();
    }

    public MessageSignatureMode Mode { get; }

    /// <summary>Expected <c>aud</c> claim; published as <c>transportAuth.audience</c>.</summary>
    public string Audience { get; }

    /// <summary>
    /// When set, the payload <c>iss</c> must equal this value (typical AiProxy client:
    /// <see cref="McpMessageSigner.DefaultIssuer"/>). When null, any non-empty <c>iss</c> is accepted.
    /// </summary>
    public string? ExpectedIssuer { get; }

    /// <summary>Public keys by <c>kid</c> (file name without <c>.pem</c>, must equal <see cref="McpSigningKey.ComputeKeyId"/>).</summary>
    public IReadOnlyDictionary<string, ECDsa> TrustedKeys { get; }

    public bool Enabled => Mode != MessageSignatureMode.Off;

    public static MessageSignatureMode ParseMode(string? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value))
            return MessageSignatureMode.Off;

        return value.Trim().ToLowerInvariant() switch
        {
            "off" => MessageSignatureMode.Off,
            "optional" => MessageSignatureMode.Optional,
            "required" => MessageSignatureMode.Required,
            _ => throw new InvalidOperationException(
                $"{ModeDirective} must be off, optional or required (got '{value.Trim()}')."),
        };
    }

    /// <summary>
    /// Resolves the <c>MessageSignature</c>, <c>MessageSignatureAudience</c>,
    /// <c>MessageSignatureIssuer</c> and <c>TrustedSignersPath</c> directive values.
    /// <paramref name="defaultAudience"/> applies when the audience directive is empty.
    /// Throws when the mode is not off and no key is found.
    /// </summary>
    public static MessageSignatureOptions Load(
        string? mode,
        string? audience,
        string? trustedSignersPath,
        string? defaultAudience = null,
        string? expectedIssuer = null)
    {
        var parsedMode = ParseMode(mode);
        if (parsedMode == MessageSignatureMode.Off)
            return Disabled;

        var resolvedAudience = FirstNonBlank(audience, defaultAudience)
            ?? throw new InvalidOperationException($"{AudienceDirective} could not be resolved.");
        var path = FirstNonBlank(trustedSignersPath, DefaultTrustedSignersPath)!;

        var keys = LoadTrustedKeys(path);
        if (keys.Count == 0)
        {
            throw new InvalidOperationException(
                $"{ModeDirective} is {parsedMode.ToString().ToLowerInvariant()} but no trusted signer key (<kid>.pem) was found in '{path}'.");
        }

        return new MessageSignatureOptions(parsedMode, resolvedAudience, keys, FirstNonBlank(expectedIssuer, null));
    }

    /// <summary>
    /// Signature always required, for an app that serves MCP next to other routes: creates
    /// <paramref name="trustedSignersPath"/> when missing (first start) and, when it holds no key yet,
    /// returns options that reject every request (fail closed) instead of failing startup. Check
    /// <see cref="TrustedKeys"/> to warn the operator. Keys are read once: restart after adding one.
    /// Defaults <see cref="ExpectedIssuer"/> to <see cref="McpMessageSigner.DefaultIssuer"/>.
    /// </summary>
    public static MessageSignatureOptions LoadRequiredOrInitialize(
        string audience,
        string? trustedSignersPath,
        string? expectedIssuer = McpMessageSigner.DefaultIssuer)
    {
        Guard.NotNullOrWhiteSpace(audience, nameof(audience));
        var path = FirstNonBlank(trustedSignersPath, DefaultTrustedSignersPath)!;
        Directory.CreateDirectory(path);
        return new MessageSignatureOptions(
            MessageSignatureMode.Required,
            audience.Trim(),
            LoadTrustedKeys(path),
            expectedIssuer);
    }

    /// <summary>
    /// Loads every <c>&lt;kid&gt;.pem</c> (SPKI <c>PUBLIC KEY</c>, P-256) in <paramref name="directory"/>.
    /// The file stem must equal <see cref="McpSigningKey.ComputeKeyId"/> of the key material.
    /// </summary>
    public static IReadOnlyDictionary<string, ECDsa> LoadTrustedKeys(string directory)
    {
        Guard.NotNull(directory, nameof(directory));
        var keys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
            return keys;

        foreach (var file in Directory.EnumerateFiles(directory, "*.pem").OrderBy(f => f, StringComparer.Ordinal))
        {
            var kid = Path.GetFileNameWithoutExtension(file);
            ECDsa key;
            try
            {
                key = P256KeyEncoding.ImportPublicKeyPem(File.ReadAllText(file));
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException($"Trusted signer key '{file}' must be an ECDSA P-256 public key PEM: {ex.Message}");
            }

            if (!P256KeyEncoding.IsP256(key))
            {
                key.Dispose();
                throw new InvalidOperationException($"Trusted signer key '{file}' must be an ECDSA P-256 key.");
            }

            var computed = McpSigningKey.ComputeKeyId(key);
            if (!string.Equals(kid, computed, StringComparison.Ordinal))
            {
                key.Dispose();
                throw new InvalidOperationException(
                    $"Trusted signer key '{file}' must be named '{computed}.pem' (base64url SHA-256 of the SPKI, first 16 characters), not '{kid}.pem'.");
            }

            keys[kid] = key;
        }

        return keys;
    }

    private static string? FirstNonBlank(string? first, string? second)
    {
        if (first is not null && !string.IsNullOrWhiteSpace(first))
            return first.Trim();
        if (second is not null && !string.IsNullOrWhiteSpace(second))
            return second.Trim();
        return null;
    }
}
