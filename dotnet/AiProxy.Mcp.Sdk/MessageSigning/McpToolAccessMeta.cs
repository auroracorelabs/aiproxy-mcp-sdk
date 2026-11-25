using System.Text.Json.Nodes;

namespace AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>
/// Per-caller tool accessibility stamped on MCP <c>tools/list</c> entries via <c>_meta.accessible</c>.
/// Servers that authorize differently per signed user should mark every listed tool; clients
/// (AiProxy) read the flag so the model sees the full census and only invokes accessible tools.
/// </summary>
/// <remarks>
/// Missing <c>_meta.accessible</c> means the server did not participate: treat the tool as
/// accessible (legacy hosts that only return the caller's census, or unannotated lists).
/// </remarks>
public static class McpToolAccessMeta
{
    /// <summary>JSON key under a tool's <c>_meta</c> object.</summary>
    public const string Key = "accessible";

    /// <summary>
    /// English prefix prepended to an inaccessible tool's description so the model sees the deny
    /// in the tool schema itself.
    /// </summary>
    public const string InaccessibleDescriptionPrefix =
        "Access: inaccessible to this user. Do not call this tool. ";

    /// <summary>Curated tool-result text when the model still calls an inaccessible tool.</summary>
    public const string InaccessibleInvokeMessage = "You do not have access to this tool.";

    /// <summary>
    /// Writes <c>accessible</c> onto <paramref name="meta"/> (creates the object when null) and
    /// returns it for assignment back onto the protocol tool.
    /// </summary>
    public static JsonObject SetAccessible(JsonObject? meta, bool accessible)
    {
        meta ??= new JsonObject();
        meta[Key] = accessible;
        return meta;
    }

    /// <summary>
    /// Reads <c>_meta.accessible</c>. Returns <see langword="null"/> when the key is absent or not a boolean.
    /// </summary>
    public static bool? TryGetAccessible(JsonObject? meta)
    {
        if (meta is null)
            return null;
        if (!meta.TryGetPropertyValue(Key, out var node) || node is null)
            return null;
        return node.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>
    /// True when the tool is usable for this caller. Missing annotation defaults to accessible.
    /// </summary>
    public static bool IsAccessibleOrDefault(JsonObject? meta) =>
        TryGetAccessible(meta) ?? true;

    /// <summary>
    /// Prepends <see cref="InaccessibleDescriptionPrefix"/> when <paramref name="accessible"/> is
    /// false and the description does not already start with it.
    /// </summary>
    public static string StampDescription(string? description, bool accessible)
    {
        var body = description ?? string.Empty;
        if (accessible)
            return body;
        if (body.StartsWith(InaccessibleDescriptionPrefix, StringComparison.Ordinal))
            return body;
        return InaccessibleDescriptionPrefix + body;
    }
}
