using System.Text;
using AiProxy.Mcp.Sdk.MessageSigning;

namespace AiProxy.Mcp.Sdk.Tests;

/// <summary>The SDK signer (client side) and verifier (server side) agree on the wire format.</summary>
public sealed class SignerVerifierRoundTripTests : IDisposable
{
    private readonly McpSigningKey _key = McpSigningKey.Generate();
    private readonly MessageSigningTestKit _kit = new();

    private MessageSignatureVerifier Verifier() => new(
        new MessageSignatureOptions(MessageSignatureMode.Required, "example-mcp", new Dictionary<string, System.Security.Cryptography.ECDsa> { [_key.KeyId] = PublicKey() }),
        new JtiReplayCache(),
        _kit.Time);

    private System.Security.Cryptography.ECDsa PublicKey()
    {
        var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(_key.PublicKeyPem);
        return key;
    }

    [Fact]
    public void Verified_identity_carries_every_signed_user_claim()
    {
        var body = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var caller = new McpCallerIdentity("6f1c2a1e-0000-4000-8000-000000000001", "mrossi", "Mario Rossi", "mario@example.test", ["Sales"], [McpCallerIdentity.RoleUser]);
        var token = new McpMessageSigner(_key, _kit.Time).Sign("POST", "/mcp", body, "example-mcp", caller);

        var result = Verifier().Verify(token, "POST", "/mcp", body);

        Assert.True(result.Success, result.Error);
        var identity = result.Identity!;
        Assert.Equal("mrossi", identity.Username);
        Assert.Equal("Mario Rossi", identity.Name);
        Assert.Equal("6f1c2a1e-0000-4000-8000-000000000001", identity.UserId);
        Assert.Equal(["Sales"], identity.Groups);
        Assert.Equal("mrossi", identity.ToPrincipal().FindFirst(MessageSignatureIdentity.ClaimUsername)?.Value);
    }

    [Fact]
    public void Service_call_has_no_username_and_role_service()
    {
        var token = new McpMessageSigner(_key, _kit.Time).Sign("POST", "/mcp", [], "example-mcp", McpCallerIdentity.Service);

        var result = Verifier().Verify(token, "POST", "/mcp", []);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Identity!.Username);
        Assert.Equal([McpCallerIdentity.RoleService], result.Identity.Roles);
    }

    [Fact]
    public void Token_for_another_audience_is_rejected()
    {
        var token = new McpMessageSigner(_key, _kit.Time).Sign("POST", "/mcp", [], "example-01", McpCallerIdentity.Service);

        Assert.Equal("audience mismatch", Verifier().Verify(token, "POST", "/mcp", []).Error);
    }

    public void Dispose()
    {
        _key.Dispose();
        _kit.Dispose();
    }
}

public sealed class LoadRequiredOrInitializeTests
{
    [Fact]
    public void First_start_creates_the_directory_and_rejects_every_request()
    {
        var root = Directory.CreateTempSubdirectory("signers-root-");
        try
        {
            var path = Path.Combine(root.FullName, "trusted-signers");

            var options = MessageSignatureOptions.LoadRequiredOrInitialize("example-mcp", path);

            Assert.True(Directory.Exists(path));
            Assert.Equal(MessageSignatureMode.Required, options.Mode);
            Assert.Equal(McpMessageSigner.DefaultIssuer, options.ExpectedIssuer);
            Assert.Empty(options.TrustedKeys);
            using var key = McpSigningKey.Generate();
            var token = new McpMessageSigner(key).Sign("POST", "/mcp", [], "example-mcp", McpCallerIdentity.Service);
            Assert.Equal("unknown kid", new MessageSignatureVerifier(options, new JtiReplayCache()).Verify(token, "POST", "/mcp", []).Error);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Installed_key_is_loaded()
    {
        var dir = Directory.CreateTempSubdirectory("signers-");
        try
        {
            using var key = McpSigningKey.Generate();
            File.WriteAllText(Path.Combine(dir.FullName, key.KeyId + ".pem"), key.PublicKeyPem);

            var options = MessageSignatureOptions.LoadRequiredOrInitialize("example-mcp", dir.FullName);

            Assert.Equal([key.KeyId], options.TrustedKeys.Keys);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
