using System.Text.Json;
using System.Text.Json.Serialization;
using AiProxy.Mcp.Sdk.ConnectionManifest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>
/// Maps <c>GET /.well-known/mcp-connection.json</c> for non-plugin HTTP MCP hosts.
/// Stamps <c>transportAuth</c> from <see cref="MessageSignatureOptions"/> when signing is enabled.
/// </summary>
public static class ConnectionManifestEndpointExtensions
{
    /// <summary>
    /// Serves the connection manifest anonymously (unsigned). When signing is enabled, overlays
    /// <c>transportAuth</c> from the registered <see cref="MessageSignatureOptions"/>.
    /// </summary>
    public static IEndpointConventionBuilder MapMcpConnectionManifest(
        this IEndpointRouteBuilder endpoints,
        McpConnectionManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(document);

        return endpoints.MapGet(
            MessageSignatureMiddleware.ManifestPath,
            CreateHandler(document));
    }

    private static RequestDelegate CreateHandler(McpConnectionManifestDocument document) =>
        async context =>
        {
            var signature = context.RequestServices.GetService<MessageSignatureOptions>();
            var wire = CloneForWire(document);
            if (signature is { Enabled: true })
            {
                wire.TransportAuth = new McpConnectionTransportAuth
                {
                    Kind = "message_signed",
                    Audience = signature.Audience,
                    Alg = MessageSignatureVerifier.Algorithm,
                };
            }
            else
            {
                wire.TransportAuth = null;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                wire,
                McpConnectionManifestJsonContext.Default.McpConnectionManifestDocument)
                .ConfigureAwait(false);
        };

    private static McpConnectionManifestDocument CloneForWire(McpConnectionManifestDocument source) =>
        new()
        {
            ConnectionSchemaVersion = source.ConnectionSchemaVersion,
            Identity = source.Identity is null
                ? null
                : new McpConnectionIdentity
                {
                    Id = source.Identity.Id,
                    DisplayName = source.Identity.DisplayName,
                    Portable = source.Identity.Portable,
                    IntegrationType = source.Identity.IntegrationType,
                },
            Auth = source.Auth is null
                ? null
                : new McpConnectionAuth { Kind = source.Auth.Kind },
            PlatformPrerequisites = source.PlatformPrerequisites,
            OAuth = source.OAuth,
            Inject = source.Inject,
            Derived = source.Derived,
            InstanceConfig = source.InstanceConfig,
            TransportAuth = null,
        };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpConnectionManifestDocument))]
[JsonSerializable(typeof(McpConnectionIdentity))]
[JsonSerializable(typeof(McpConnectionAuth))]
[JsonSerializable(typeof(McpConnectionTransportAuth))]
[JsonSerializable(typeof(McpPlatformPrerequisite))]
[JsonSerializable(typeof(McpConnectionOAuth))]
[JsonSerializable(typeof(McpConnectionInject))]
[JsonSerializable(typeof(McpConnectionDerived))]
[JsonSerializable(typeof(McpConfigParameter))]
[JsonSerializable(typeof(List<McpPlatformPrerequisite>))]
[JsonSerializable(typeof(List<McpConnectionInject>))]
[JsonSerializable(typeof(List<McpConnectionDerived>))]
[JsonSerializable(typeof(List<McpConfigParameter>))]
internal sealed partial class McpConnectionManifestJsonContext : JsonSerializerContext;
