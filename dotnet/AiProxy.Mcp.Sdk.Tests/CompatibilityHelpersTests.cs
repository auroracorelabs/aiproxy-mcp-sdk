using System.Security.Cryptography;
using AiProxy.Mcp.Sdk.Internal;
using AiProxy.Mcp.Sdk.MessageSigning;
using NativeBase64Url = System.Buffers.Text.Base64Url;

namespace AiProxy.Mcp.Sdk.Tests;

/// <summary>
/// The netstandard2.0-compatible helpers must match the native .NET implementations byte for byte,
/// so keys, key ids and signatures stay interchangeable with every other implementation.
/// </summary>
public sealed class CompatibilityHelpersTests
{
    [Fact]
    public void Public_key_encodings_match_native()
    {
        for (var i = 0; i < 25; i++)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            Assert.Equal(key.ExportSubjectPublicKeyInfo(), P256KeyEncoding.ExportSubjectPublicKeyInfo(key));
            Assert.Equal(key.ExportSubjectPublicKeyInfoPem(), P256KeyEncoding.ExportPublicKeyPem(key));
        }
    }

    [Fact]
    public void Private_key_export_is_readable_by_native_and_back()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var expected = key.ExportParameters(includePrivateParameters: true);

        using var native = ECDsa.Create();
        native.ImportFromPem(P256KeyEncoding.ExportPkcs8PrivateKeyPem(key));
        Assert.Equal(expected.D, native.ExportParameters(true).D);

        foreach (var pem in new[] { key.ExportPkcs8PrivateKeyPem(), key.ExportECPrivateKeyPem() })
        {
            using var imported = P256KeyEncoding.ImportPrivateKeyPem(pem);
            var p = imported.ExportParameters(true);
            Assert.Equal(expected.D, p.D);
            Assert.Equal(expected.Q.X, p.Q.X);
            Assert.Equal(expected.Q.Y, p.Q.Y);
        }
    }

    [Fact]
    public void Imported_public_key_verifies_native_signatures()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = "payload"u8.ToArray();
        var signature = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        using var imported = P256KeyEncoding.ImportPublicKeyPem(key.ExportSubjectPublicKeyInfoPem());

        Assert.True(imported.VerifyData(data, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Key_id_is_unchanged_from_the_native_derivation()
    {
        using var signing = McpSigningKey.Generate();
        var native = NativeBase64Url.EncodeToString(SHA256.HashData(signing.Key.ExportSubjectPublicKeyInfo()))[..16];

        Assert.Equal(native, signing.KeyId);
    }

    [Fact]
    public void Signing_key_pem_round_trips()
    {
        using var signing = McpSigningKey.Generate();
        using var reloaded = McpSigningKey.FromPem(P256KeyEncoding.ExportPkcs8PrivateKeyPem(signing.Key));

        Assert.Equal(signing.KeyId, reloaded.KeyId);
    }

    [Fact]
    public void Non_p256_and_malformed_keys_are_rejected()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<CryptographicException>(() => P256KeyEncoding.ImportPublicKeyPem(p384.ExportSubjectPublicKeyInfoPem()));
        Assert.Throws<CryptographicException>(() => P256KeyEncoding.ImportPrivateKeyPem(p384.ExportPkcs8PrivateKeyPem()));
        using var rsa = RSA.Create();
        Assert.Throws<CryptographicException>(() => P256KeyEncoding.ImportPublicKeyPem(rsa.ExportSubjectPublicKeyInfoPem()));

        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = p256.ExportSubjectPublicKeyInfo();
        Assert.Throws<CryptographicException>(() => P256KeyEncoding.ImportSubjectPublicKeyInfo(spki[..^1]));
        Assert.Throws<CryptographicException>(() => P256KeyEncoding.ImportSubjectPublicKeyInfo([.. spki, 0x00]));
        Assert.Throws<CryptographicException>(() => P256KeyEncoding.ImportPublicKeyPem("not a pem"));
        Assert.Throws<InvalidOperationException>(() => McpSigningKey.FromPem(p384.ExportPkcs8PrivateKeyPem()));
    }

    [Fact]
    public void Base64url_matches_native_and_is_strict()
    {
        var random = new Random(1);
        for (var length = 0; length < 70; length++)
        {
            var data = new byte[length];
            random.NextBytes(data);
            var native = NativeBase64Url.EncodeToString(data);

            Assert.Equal(native, Base64UrlCodec.Encode(data));
            Assert.True(Base64UrlCodec.TryDecode(native, out var decoded));
            Assert.Equal(data, decoded);
        }

        Assert.False(Base64UrlCodec.TryDecode("ab==", out _));
        Assert.False(Base64UrlCodec.TryDecode("a+b/", out _));
        Assert.False(Base64UrlCodec.TryDecode("abcde", out _));
    }

    [Fact]
    public void Constant_time_compare()
    {
        Assert.True(ConstantTime.Equals([1, 2, 3], [1, 2, 3]));
        Assert.False(ConstantTime.Equals([1, 2, 3], [1, 2, 4]));
        Assert.False(ConstantTime.Equals([1, 2, 3], [1, 2]));
    }
}
