using System.Security.Cryptography;
using System.Text;
using AiProxy.Mcp.Sdk.MessageSigning;

namespace AiProxy.Mcp.Sdk.Tests;

public sealed class MessageSignatureVerifierTests : IDisposable
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
    private readonly MessageSigningTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public void Valid_request_yields_identity()
    {
        var result = _kit.Verifier().Verify(_kit.Sign("POST", "/", Body), "POST", "/", Body);

        Assert.True(result.Success, result.Error);
        var id = result.Identity!;
        Assert.Equal("6f1c2a1e-0000-4000-8000-000000000001", id.UserId);
        Assert.Equal("Mario Rossi", id.Name);
        Assert.Equal(["Sales", "Wiki editors"], id.Groups);
        Assert.Equal(["user"], id.Roles);
        Assert.Equal(MessageSigningTestKit.Kid, id.KeyId);

        var principal = id.ToPrincipal();
        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal("Mario Rossi", principal.Identity.Name);
        Assert.True(principal.IsInRole("user"));
    }

    [Fact]
    public void Tampered_body_is_rejected()
    {
        var token = _kit.Sign("POST", "/", Body);
        var tampered = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"tools/call"}""");

        Assert.Equal("body hash mismatch", _kit.Verifier().Verify(token, "POST", "/", tampered).Error);
    }

    [Fact]
    public void Tampered_payload_breaks_signature()
    {
        var token = _kit.Sign("POST", "/", Body);
        var parts = token.Split('.');
        var forged = _kit.Sign("POST", "/", Body, p => p["sub"] = "someone-else").Split('.')[1];

        var result = _kit.Verifier().Verify(parts[0] + "." + forged + "." + parts[2], "POST", "/", Body);

        Assert.Equal("bad signature", result.Error);
    }

    [Theory]
    [InlineData("GET", "/", "method mismatch")]
    [InlineData("POST", "/other", "path mismatch")]
    [InlineData("POST", "/?x=1", "path mismatch")]
    public void Method_and_path_are_bound(string method, string path, string error)
    {
        var token = _kit.Sign("POST", "/", Body);
        Assert.Equal(error, _kit.Verifier().Verify(token, method, path, Body).Error);
    }

    [Fact]
    public void Wrong_audience_is_rejected()
    {
        var token = _kit.Sign("POST", "/", Body, p => p["aud"] = "other-instance");
        Assert.Equal("audience mismatch", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Unknown_kid_is_rejected()
    {
        var token = _kit.Sign("POST", "/", Body, kid: "rotated-away");
        Assert.Equal("unknown kid", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Key_not_trusted_is_rejected()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = _kit.Sign("POST", "/", Body, key: attacker);
        Assert.Equal("bad signature", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("ES384")]
    public void Only_es256_is_accepted(string alg)
    {
        var token = _kit.Sign("POST", "/", Body, alg: alg);
        Assert.Equal("unsupported alg", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Replayed_jti_is_rejected()
    {
        var verifier = _kit.Verifier();
        var token = _kit.Sign("POST", "/", Body);

        Assert.True(verifier.Verify(token, "POST", "/", Body).Success);
        Assert.Equal("replayed jti", verifier.Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Expired_request_is_rejected_after_skew()
    {
        var token = _kit.Sign("POST", "/", Body);
        _kit.Time.Now = _kit.Time.Now.AddSeconds(60 + 31);
        Assert.Equal("expired", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Expired_request_within_skew_is_accepted()
    {
        var token = _kit.Sign("POST", "/", Body);
        _kit.Time.Now = _kit.Time.Now.AddSeconds(60 + 29);
        Assert.True(_kit.Verifier().Verify(token, "POST", "/", Body).Success);
    }

    [Fact]
    public void Future_request_is_rejected()
    {
        var token = _kit.Sign("POST", "/", Body);
        _kit.Time.Now = _kit.Time.Now.AddSeconds(-31);
        Assert.Equal("issued in the future", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Too_long_lifetime_is_rejected()
    {
        var token = _kit.Sign("POST", "/", Body, p => p["exp"] = (long)p["iat"]! + 3600);
        Assert.Equal("invalid lifetime", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Unsupported_payload_version_is_rejected()
    {
        var token = _kit.Sign("POST", "/", Body, p => p["v"] = 2);
        Assert.Equal("unsupported payload version", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Fact]
    public void Missing_jti_is_rejected()
    {
        var token = _kit.Sign("POST", "/", Body, p => p.Remove("jti"));
        Assert.Equal("missing jti", _kit.Verifier().Verify(token, "POST", "/", Body).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a.b")]
    [InlineData("a..c")]
    [InlineData("!!!.###.$$$")]
    public void Malformed_tokens_are_rejected(string token)
    {
        Assert.False(_kit.Verifier().Verify(token, "POST", "/", Body).Success);
    }

    [Fact]
    public void Service_identity_without_user_is_accepted()
    {
        var token = _kit.Sign("POST", "/", Body, p =>
        {
            p.Remove("sub");
            p.Remove("name");
            p.Remove("email");
            p.Remove("groups");
            p["roles"] = new[] { "service" };
        });

        var result = _kit.Verifier().Verify(token, "POST", "/", Body);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Identity!.UserId);
        Assert.Equal(["service"], result.Identity.Roles);
    }

    [Fact]
    public void Replay_cache_fails_closed_when_full_of_live_entries()
    {
        var cache = new JtiReplayCache(capacity: 1);
        Assert.True(cache.TryRegister("a", forgetAfterUnix: 200, nowUnix: 100));
        Assert.False(cache.TryRegister("b", forgetAfterUnix: 200, nowUnix: 100));
        Assert.True(cache.TryRegister("b", forgetAfterUnix: 400, nowUnix: 300)); // "a" swept
    }
}
