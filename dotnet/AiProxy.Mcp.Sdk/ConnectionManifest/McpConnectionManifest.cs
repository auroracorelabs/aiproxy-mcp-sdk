using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiProxy.Mcp.Sdk.ConnectionManifest;

/// <summary>
/// AiProxy connection-manifest v1 (camelCase wire). Setup companion for wizards / inject —
/// not MCP <c>initialize</c> branding (see <c>docs/MCP_IDENTITY.md</c>).
/// </summary>
public sealed class McpConnectionManifestDocument
{
    [JsonPropertyName("connectionSchemaVersion")]
    public int ConnectionSchemaVersion { get; set; } = 1;

    [JsonPropertyName("identity")]
    public McpConnectionIdentity? Identity { get; set; }

    [JsonPropertyName("auth")]
    public McpConnectionAuth? Auth { get; set; }

    /// <summary>Host-stamped transport authentication; plugins must leave this null.</summary>
    [JsonPropertyName("transportAuth")]
    public McpConnectionTransportAuth? TransportAuth { get; set; }

    [JsonPropertyName("platformPrerequisites")]
    public List<McpPlatformPrerequisite>? PlatformPrerequisites { get; set; }

    [JsonPropertyName("oauth")]
    public McpConnectionOAuth? OAuth { get; set; }

    [JsonPropertyName("inject")]
    public List<McpConnectionInject>? Inject { get; set; }

    [JsonPropertyName("derived")]
    public List<McpConnectionDerived>? Derived { get; set; }

    [JsonPropertyName("instanceConfig")]
    public List<McpConfigParameter>? InstanceConfig { get; set; }
}

public sealed class McpConnectionIdentity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("portable")]
    public bool? Portable { get; set; }

    [JsonPropertyName("integrationType")]
    public string? IntegrationType { get; set; }
}

public sealed class McpConnectionAuth
{
    /// <summary><c>none</c> | <c>instance_config</c> | <c>mcp_oauth</c> | <c>platform_oauth</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "none";
}

public sealed class McpConnectionTransportAuth
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("audience")]
    public string? Audience { get; set; }

    [JsonPropertyName("alg")]
    public string? Alg { get; set; }
}

public sealed class McpPlatformPrerequisite
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

public sealed class McpConnectionOAuth
{
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("scopes")]
    public string? Scopes { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }
}

public sealed class McpConnectionInject
{
    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("intoToolProperty")]
    public string IntoToolProperty { get; set; } = "";
}

public sealed class McpConnectionDerived
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";
}

public sealed class McpConfigParameter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }

    [JsonPropertyName("default")]
    public string? Default { get; set; }

    [JsonPropertyName("secret")]
    public bool? Secret { get; set; }
}
