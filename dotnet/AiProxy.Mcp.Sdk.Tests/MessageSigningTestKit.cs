using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiProxy.Mcp.Sdk.MessageSigning;

namespace AiProxy.Mcp.Sdk.Tests;

/// <summary>Reference signer used by tests (mirrors what AiProxy produces).</summary>
internal sealed class MessageSigningTestKit : IDisposable
{
    public const string Kid = "aiproxy-test";
    public const string Audience = "example-01";

    public MessageSigningTestKit()
    {
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PublicKey = ECDsa.Create();
        PublicKey.ImportSubjectPublicKeyInfo(Key.ExportSubjectPublicKeyInfo(), out _);
        Time = new ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_763_700_000));
    }

    public ECDsa Key { get; }

    public ECDsa PublicKey { get; }

    public ManualTimeProvider Time { get; }

    public MessageSignatureOptions Options(
        MessageSignatureMode mode = MessageSignatureMode.Required,
        string? expectedIssuer = null) =>
        new(mode, Audience, new Dictionary<string, ECDsa> { [Kid] = PublicKey }, expectedIssuer);

    public MessageSignatureVerifier Verifier(
        MessageSignatureMode mode = MessageSignatureMode.Required,
        JtiReplayCache? cache = null,
        string? expectedIssuer = null) =>
        new(Options(mode, expectedIssuer), cache ?? new JtiReplayCache(), Time);

    public string Sign(
        string method,
        string path,
        byte[] body,
        Action<Dictionary<string, object?>>? mutatePayload = null,
        string alg = "ES256",
        string kid = Kid,
        ECDsa? key = null)
    {
        var now = Time.GetUtcNow().ToUnixTimeSeconds();
        var payload = new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["iss"] = "aiproxy",
            ["aud"] = Audience,
            ["iat"] = now,
            ["exp"] = now + 60,
            ["jti"] = Guid.NewGuid().ToString("D"),
            ["htm"] = method,
            ["htp"] = path,
            ["bh"] = Base64Url.EncodeToString(SHA256.HashData(body)),
            ["sub"] = "6f1c2a1e-0000-4000-8000-000000000001",
            ["username"] = "mrossi",
            ["name"] = "Mario Rossi",
            ["email"] = "mario@example.test",
            ["groups"] = new[] { "Sales", "Wiki editors" },
            ["roles"] = new[] { "user" },
        };
        mutatePayload?.Invoke(payload);

        var header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string> { ["alg"] = alg, ["typ"] = "mcp-msg+jwt", ["kid"] = kid }));
        var body64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = (key ?? Key).SignData(
            Encoding.ASCII.GetBytes(header + "." + body64),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return header + "." + body64 + "." + Base64Url.EncodeToString(signature);
    }

    public void Dispose()
    {
        Key.Dispose();
        PublicKey.Dispose();
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
