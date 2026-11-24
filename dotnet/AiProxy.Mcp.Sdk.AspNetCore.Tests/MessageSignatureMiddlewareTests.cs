using System.Security.Cryptography;
using System.Text;
using AiProxy.Mcp.Sdk.MessageSigning;
using AiProxy.Mcp.Sdk.Tests;
using Microsoft.AspNetCore.Http;

namespace AiProxy.Mcp.Sdk.AspNetCore.Tests;

public sealed class MessageSignatureMiddlewareTests : IDisposable
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
    private readonly MessageSigningTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private async Task<(HttpContext Context, bool NextCalled, string? DownstreamBody)> RunAsync(
        MessageSignatureMode mode,
        string? signature,
        byte[]? body = null,
        string method = "POST",
        string path = "/",
        string? extraSignature = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Body = new MemoryStream(body ?? Body);
        context.Response.Body = new MemoryStream();
        if (signature is not null)
            context.Request.Headers.Append(MessageSignatureVerifier.HeaderName, signature);
        if (extraSignature is not null)
            context.Request.Headers.Append(MessageSignatureVerifier.HeaderName, extraSignature);

        var nextCalled = false;
        string? downstream = null;
        await MessageSignatureMiddleware.InvokeAsync(
            context,
            async ctx =>
            {
                nextCalled = true;
                downstream = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            },
            _kit.Options(mode),
            _kit.Verifier(mode),
            logger: null);
        return (context, nextCalled, downstream);
    }

    [Fact]
    public async Task Required_valid_signature_sets_user_and_preserves_body()
    {
        var (ctx, next, downstream) = await RunAsync(MessageSignatureMode.Required, _kit.Sign("POST", "/", Body));

        Assert.True(next);
        Assert.Equal(Encoding.UTF8.GetString(Body), downstream);
        Assert.Equal(MessageSignatureIdentity.AuthenticationType, ctx.User.Identity!.AuthenticationType);
        Assert.Equal("6f1c2a1e-0000-4000-8000-000000000001", ctx.User.FindFirst(MessageSignatureIdentity.ClaimSubject)!.Value);
        Assert.Equal(["Sales", "Wiki editors"], ctx.User.FindAll(MessageSignatureIdentity.ClaimGroup).Select(c => c.Value));
    }

    [Fact]
    public async Task Required_unsigned_is_401()
    {
        var (ctx, next, _) = await RunAsync(MessageSignatureMode.Required, signature: null);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.Equal("MessageSigned", ctx.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal("""{"error":"unauthorized"}""", Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray()));
    }

    [Fact]
    public async Task Required_invalid_is_401()
    {
        var token = _kit.Sign("POST", "/", Body);
        var (ctx, next, _) = await RunAsync(MessageSignatureMode.Required, token, body: Encoding.UTF8.GetBytes("{}"));

        Assert.False(next);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        // Failure detail stays in logs only — body must not name body-hash / audience / etc.
        Assert.Equal("""{"error":"unauthorized"}""", Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray()));
    }

    [Fact]
    public async Task Optional_unsigned_passes_anonymously()
    {
        var (ctx, next, downstream) = await RunAsync(MessageSignatureMode.Optional, signature: null);

        Assert.True(next);
        Assert.Equal(Encoding.UTF8.GetString(Body), downstream);
        Assert.False(ctx.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task Optional_present_but_invalid_is_401()
    {
        var token = _kit.Sign("POST", "/", Body, p => p["aud"] = "elsewhere");
        var (ctx, next, _) = await RunAsync(MessageSignatureMode.Optional, token);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Multiple_signature_headers_are_401()
    {
        var a = _kit.Sign("POST", "/", Body);
        var b = _kit.Sign("POST", "/", Body);
        var (ctx, next, _) = await RunAsync(MessageSignatureMode.Required, a, extraSignature: b);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Manifest_is_public_when_required()
    {
        var (_, next, _) = await RunAsync(
            MessageSignatureMode.Required,
            signature: null,
            body: [],
            method: "GET",
            path: MessageSignatureMiddleware.ManifestPath);

        Assert.True(next);
    }

    [Fact]
    public async Task Oversize_body_is_413()
    {
        var big = new byte[MessageSignatureMiddleware.MaxBodyBytes + 1];
        var (ctx, next, _) = await RunAsync(MessageSignatureMode.Required, _kit.Sign("POST", "/", big), body: big);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ctx.Response.StatusCode);
    }
}
