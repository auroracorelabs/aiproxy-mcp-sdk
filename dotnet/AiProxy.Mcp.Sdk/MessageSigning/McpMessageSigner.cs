using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AiProxy.Mcp.Sdk.Internal;
using System.Text;
using System.Text.Json;

namespace AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>
/// Acting caller asserted to <c>message_signed</c> MCP servers. <see cref="UserId"/> is null for
/// calls without a user, which are sent with role <c>service</c>. <see cref="Username"/> is the
/// login name (claim <c>username</c>), the key an MCP uses to look up its own permissions.
/// </summary>
public sealed record McpCallerIdentity(
    string? UserId,
    string? Username,
    string? Name,
    string? Email,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Roles)
{
    public const string RoleUser = "user";
    public const string RoleAdmin = "admin";
    public const string RoleService = "service";

    public static McpCallerIdentity Service { get; } = new(null, null, null, null, [], [RoleService]);
}

/// <summary>
/// The client's ES256 key for <c>message_signed</c>. Private key: PKCS#8 PEM file (conventionally
/// <c>mcp-signing-key.pem</c>),
/// generated on first start with mode 0600. <see cref="KeyId"/> = first 16 characters of base64url(SHA-256(SPKI)).
/// The public key is installed on MCP hosts as <c>/etc/mcp/trusted-signers/&lt;kid&gt;.pem</c>.
/// </summary>
public sealed class McpSigningKey : IDisposable
{
    public const string DefaultFileName = "mcp-signing-key.pem";

    private McpSigningKey(ECDsa key)
    {
        Key = key;
        KeyId = ComputeKeyId(key);
    }

    public ECDsa Key { get; }

    public string KeyId { get; }

    public string PublicKeyPem => P256KeyEncoding.ExportPublicKeyPem(Key);

    public static McpSigningKey Generate() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static McpSigningKey FromPem(string pem)
    {
        Guard.NotNull(pem, nameof(pem));
        ECDsa key;
        try
        {
            key = P256KeyEncoding.ImportPrivateKeyPem(pem);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException($"MCP signing key must be an ECDSA P-256 private key (PKCS#8 or SEC1 PEM): {ex.Message}", ex);
        }

        if (!P256KeyEncoding.IsP256(key))
        {
            key.Dispose();
            throw new InvalidOperationException("MCP signing key must be an ECDSA P-256 private key.");
        }

        return new McpSigningKey(key);
    }

    /// <summary>
    /// Loads the key, creating it (mode 0600) on first start. On Unix, creating it needs a .NET 7+
    /// runtime to set the file mode; older runtimes refuse rather than leave the key readable by others.
    /// </summary>
    public static McpSigningKey LoadOrCreate(string path)
    {
        Guard.NotNullOrWhiteSpace(path, nameof(path));
        if (File.Exists(path))
            return FromPem(File.ReadAllText(path));

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#if !NET7_0_OR_GREATER
        if (!isWindows)
        {
            throw new InvalidOperationException(
                $"MCP signing key '{path}' does not exist and this runtime cannot create it with mode 0600. " +
                "Create it with 'openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out <path>' and 'chmod 600 <path>'.");
        }
#endif

        var created = Generate();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        var pem = Encoding.ASCII.GetBytes(P256KeyEncoding.ExportPkcs8PrivateKeyPem(created.Key));
        using (var stream = OpenPrivateKeyWriteStream(tmp, isWindows))
            stream.Write(pem, 0, pem.Length);

        File.Move(tmp, path);
        return created;
    }

#if NET7_0_OR_GREATER
    private static FileStream OpenPrivateKeyWriteStream(string path, bool isWindows)
    {
        if (!isWindows
            && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()))
        {
            return new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                // Mode 0600 at create — no window where the private key is group/world readable.
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
        }

        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }
#else
    private static FileStream OpenPrivateKeyWriteStream(string path, bool _) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None);
#endif

    /// <summary>First 16 characters of base64url(SHA-256(SPKI DER)) — the wire <c>kid</c> and the required trusted-signers file stem.</summary>
    public static string ComputeKeyId(ECDsa key)
    {
        Guard.NotNull(key, nameof(key));
        return Base64UrlCodec.Encode(Sha256.Hash(P256KeyEncoding.ExportSubjectPublicKeyInfo(key))).Substring(0, 16);
    }

    public void Dispose() => Key.Dispose();
}

/// <summary>
/// Produces the <c>Mcp-Message-Signature</c> header value (compact JWS, ES256) — wire format in
/// <c>docs/MESSAGE_SIGNED.md</c> in the SDK repository.
/// </summary>
public sealed class McpMessageSigner
{
    public const string HeaderName = "Mcp-Message-Signature";
    /// <summary>Default <c>iss</c> for AiProxy as the signing client.</summary>
    public const string DefaultIssuer = "aiproxy";
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly McpSigningKey _key;
    private readonly string _issuer;
    private readonly TimeProvider _time;
    private readonly string _encodedHeader;

    public McpMessageSigner(McpSigningKey key, TimeProvider? time = null)
        : this(key, DefaultIssuer, time)
    {
    }

    public McpMessageSigner(McpSigningKey key, string issuer, TimeProvider? time = null)
    {
        _key = Guard.NotNull(key, nameof(key));
        _issuer = Guard.NotNullOrWhiteSpace(issuer, nameof(issuer));
        _time = time ?? TimeProvider.System;
        _encodedHeader = Base64UrlCodec.Encode(WriteJson(w =>
        {
            w.WriteString("alg", "ES256");
            w.WriteString("typ", "mcp-msg+jwt");
            w.WriteString("kid", key.KeyId);
        }));
    }

    public string KeyId => _key.KeyId;

    public string Issuer => _issuer;

    public string Sign(
        string method,
        string pathAndQuery,
        ReadOnlySpan<byte> body,
        string audience,
        McpCallerIdentity caller)
    {
        Guard.NotNullOrWhiteSpace(method, nameof(method));
        Guard.NotNullOrWhiteSpace(audience, nameof(audience));
        Guard.NotNull(caller, nameof(caller));

        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var bodyHash = Base64UrlCodec.Encode(Sha256.Hash(body.ToArray()));
        var payload = Base64UrlCodec.Encode(WriteJson(w =>
        {
            w.WriteNumber("v", 1);
            w.WriteString("iss", _issuer);
            w.WriteString("aud", audience);
            w.WriteNumber("iat", now);
            w.WriteNumber("exp", now + (long)Lifetime.TotalSeconds);
            w.WriteString("jti", Guid.NewGuid().ToString("D"));
            w.WriteString("htm", method.ToUpperInvariant());
            w.WriteString("htp", string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery);
            w.WriteString("bh", bodyHash);
            if (!string.IsNullOrEmpty(caller.UserId))
                w.WriteString("sub", caller.UserId);
            if (!string.IsNullOrEmpty(caller.Username))
                w.WriteString("username", caller.Username);
            if (!string.IsNullOrEmpty(caller.Name))
                w.WriteString("name", caller.Name);
            if (!string.IsNullOrEmpty(caller.Email))
                w.WriteString("email", caller.Email);
            WriteArray(w, "groups", caller.Groups);
            WriteArray(w, "roles", caller.Roles);
        }));

        var signingInput = _encodedHeader + "." + payload;
        // ECDsa.SignData without a format argument yields IEEE P1363 (r||s, 64 bytes) on every runtime.
        var signature = _key.Key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return signingInput + "." + Base64UrlCodec.Encode(signature);
    }

    private static void WriteArray(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values)
            w.WriteStringValue(value);
        w.WriteEndArray();
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            write(w);
            w.WriteEndObject();
        }

        return ms.ToArray();
    }
}

/// <summary>
/// Signs every outgoing request of one MCP session (body buffered and re-attached unchanged, so the
/// hash covers the exact bytes sent).
/// </summary>
public sealed class McpSigningHandler : DelegatingHandler
{
    private readonly McpMessageSigner _signer;
    private readonly string _audience;
    private readonly McpCallerIdentity _caller;

    public McpSigningHandler(McpMessageSigner signer, string audience, McpCallerIdentity caller, HttpMessageHandler inner)
        : base(inner)
    {
        _signer = signer;
        _audience = audience;
        _caller = caller;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = [];
        if (request.Content is { } content)
        {
#if NET5_0_OR_GREATER
            body = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            body = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            var replacement = new ByteArrayContent(body);
            foreach (var header in content.Headers)
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = replacement;
        }

        var uri = request.RequestUri ?? throw new InvalidOperationException("MCP request without URI.");
        request.Headers.Remove(McpMessageSigner.HeaderName);
        request.Headers.TryAddWithoutValidation(
            McpMessageSigner.HeaderName,
            _signer.Sign(request.Method.Method, uri.PathAndQuery, body, _audience, _caller));

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The inner handler is the process-wide socket pool: never dispose it with the session.</summary>
    protected override void Dispose(bool disposing)
    {
    }
}
