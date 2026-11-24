using System.Security.Cryptography;
using AiProxy.Mcp.Sdk.MessageSigning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AiProxy.Mcp.Sdk.AspNetCore.Tests;

/// <summary>An app that serves MCP next to a UI verifies only the MCP path.</summary>
public sealed class ScopedMiddlewareTests : IDisposable
{
    private readonly McpSigningKey _key = McpSigningKey.Generate();

    [Theory]
    [InlineData("/mcp", StatusCodes.Status401Unauthorized)]
    [InlineData("/mcp/sub", StatusCodes.Status401Unauthorized)]
    [InlineData("/Reports", StatusCodes.Status200OK)]
    [InlineData("/mcpx", StatusCodes.Status200OK)]
    public async Task Scoped_middleware_only_guards_the_scope(string path, int expected)
    {
        var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(_key.PublicKeyPem);
        var services = new ServiceCollection().AddLogging()
            .AddMessageSignature(new MessageSignatureOptions(MessageSignatureMode.Required, "example-mcp", new Dictionary<string, ECDsa> { [_key.KeyId] = publicKey }))
            .BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseMessageSignature("/mcp");
        app.Run(ctx => { ctx.Response.StatusCode = StatusCodes.Status200OK; return Task.CompletedTask; });
        var pipeline = app.Build();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await pipeline(context);

        Assert.Equal(expected, context.Response.StatusCode);
    }

    public void Dispose() => _key.Dispose();
}
