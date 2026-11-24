using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>
/// Verifies <c>Mcp-Message-Signature</c> on every request except the public connection manifest, and
/// on success sets <see cref="HttpContext.User"/> (the MCP SDK forwards it as <c>MessageContext.User</c>).
/// <list type="bullet">
/// <item><c>required</c>: missing or invalid signature → 401.</item>
/// <item><c>optional</c>: missing → continue anonymously (warning logged); present but invalid → 401.</item>
/// </list>
/// </summary>
public static class MessageSignatureMiddleware
{
    public const int MaxBodyBytes = 4 * 1024 * 1024;
    public const string ManifestPath = "/.well-known/mcp-connection.json";

    public static IServiceCollection AddMessageSignature(this IServiceCollection services, MessageSignatureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton(new JtiReplayCache());
        services.AddSingleton(sp => new MessageSignatureVerifier(
            options,
            sp.GetRequiredService<JtiReplayCache>(),
            sp.GetService<TimeProvider>()));
        return services;
    }

    /// <summary>Verifies every request, or only those under <paramref name="scope"/> when given (an app that also serves a UI).</summary>
    public static IApplicationBuilder UseMessageSignature(this IApplicationBuilder app, PathString scope = default)
    {
        var options = app.ApplicationServices.GetRequiredService<MessageSignatureOptions>();
        if (!options.Enabled)
            return app;

        var verifier = app.ApplicationServices.GetRequiredService<MessageSignatureVerifier>();
        var logger = app.ApplicationServices.GetService<ILoggerFactory>()?.CreateLogger("AiProxy.Mcp.MessageSignature");

        return app.Use((context, next) => scope.HasValue && !context.Request.Path.StartsWithSegments(scope)
            ? next(context)
            : InvokeAsync(context, next, options, verifier, logger));
    }

    internal static async Task InvokeAsync(
        HttpContext context,
        RequestDelegate next,
        MessageSignatureOptions options,
        MessageSignatureVerifier verifier,
        ILogger? logger)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            && string.Equals(context.Request.Path.Value, ManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var headers = context.Request.Headers[MessageSignatureVerifier.HeaderName];
        if (headers.Count == 0)
        {
            if (options.Mode == MessageSignatureMode.Optional)
            {
                logger?.LogWarning(
                    "Unsigned MCP request accepted (MessageSignature optional) method={Method} path={Path}",
                    context.Request.Method,
                    context.Request.Path.Value);
                await next(context).ConfigureAwait(false);
                return;
            }

            await RejectAsync(context, StatusCodes.Status401Unauthorized, "missing signature", logger).ConfigureAwait(false);
            return;
        }

        if (headers.Count > 1)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "multiple signatures", logger).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        if (body is null)
        {
            await RejectAsync(context, StatusCodes.Status413PayloadTooLarge, "request body too large", logger).ConfigureAwait(false);
            return;
        }

        var pathAndQuery = context.Request.PathBase.Add(context.Request.Path).Value + context.Request.QueryString.Value;
        if (string.IsNullOrEmpty(pathAndQuery))
            pathAndQuery = "/";

        var result = verifier.Verify(headers[0], context.Request.Method, pathAndQuery, body);
        if (!result.Success)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, result.Error!, logger).ConfigureAwait(false);
            return;
        }

        context.User = result.Identity!.ToPrincipal();
        await next(context).ConfigureAwait(false);
    }

    /// <summary>Buffers the body (rewound for downstream). Null when it exceeds <see cref="MaxBodyBytes"/>.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
            return null;

        request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: MaxBodyBytes + 1);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        request.Body.Position = 0;
        return buffer.ToArray();
    }

    private static async Task RejectAsync(HttpContext context, int status, string reason, ILogger? logger)
    {
        logger?.LogWarning(
            "Rejected MCP request: {Reason} method={Method} path={Path} remote={Remote}",
            reason,
            context.Request.Method,
            context.Request.Path.Value,
            context.Connection.RemoteIpAddress);

        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        if (status == StatusCodes.Status401Unauthorized)
            context.Response.Headers.WWWAuthenticate = "MessageSigned";

        using var ms = new MemoryStream();
        await using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("error", "unauthorized");
            w.WriteString("reason", reason);
            w.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(ms.ToArray(), context.RequestAborted).ConfigureAwait(false);
    }
}
