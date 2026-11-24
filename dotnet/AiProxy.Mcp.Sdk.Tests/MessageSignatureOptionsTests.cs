using System.Security.Cryptography;
using AiProxy.Mcp.Sdk.MessageSigning;

namespace AiProxy.Mcp.Sdk.Tests;

public sealed class MessageSignatureOptionsTests : IDisposable
{
    private readonly MessageSigningTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public void Options_default_to_off_when_key_missing()
    {
        var options = MessageSignatureOptions.Load(null, null, null, "example-01");

        Assert.False(options.Enabled);
    }

    [Fact]
    public void Options_load_keys_and_default_audience_to_instance_id()
    {
        var dir = Directory.CreateTempSubdirectory("trusted-signers-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "k1.pem"), _kit.PublicKey.ExportSubjectPublicKeyInfoPem());
            var options = MessageSignatureOptions.Load("required", null, dir.FullName, "example-01");

            Assert.Equal(MessageSignatureMode.Required, options.Mode);
            Assert.Equal("example-01", options.Audience);
            Assert.Equal(["k1"], options.TrustedKeys.Keys);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Options_enabled_without_keys_fail_startup()
    {
        var dir = Directory.CreateTempSubdirectory("trusted-signers-");
        try
        {
            Assert.Throws<InvalidOperationException>(() => MessageSignatureOptions.Load("optional", null, dir.FullName, "x"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Options_reject_non_p256_keys()
    {
        var dir = Directory.CreateTempSubdirectory("trusted-signers-");
        try
        {
            using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            File.WriteAllText(Path.Combine(dir.FullName, "k.pem"), p384.ExportSubjectPublicKeyInfoPem());

            Assert.Throws<InvalidOperationException>(() => MessageSignatureOptions.LoadTrustedKeys(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("true")]
    public void Options_reject_unknown_modes(string mode) =>
        Assert.Throws<InvalidOperationException>(() => MessageSignatureOptions.ParseMode(mode));
}
