using System.Text.Json.Nodes;
using AiProxy.Mcp.Sdk.MessageSigning;

namespace AiProxy.Mcp.Sdk.Tests;

public sealed class McpToolAccessMetaTests
{
    [Fact]
    public void SetAccessible_writes_boolean_under_standard_key()
    {
        var meta = McpToolAccessMeta.SetAccessible(null, accessible: false);

        Assert.Equal(McpToolAccessMeta.Key, "accessible");
        Assert.False(meta[McpToolAccessMeta.Key]!.GetValue<bool>());
        Assert.False(McpToolAccessMeta.TryGetAccessible(meta));
        Assert.False(McpToolAccessMeta.IsAccessibleOrDefault(meta));
    }

    [Fact]
    public void TryGetAccessible_null_meta_and_missing_key_default_to_accessible()
    {
        Assert.Null(McpToolAccessMeta.TryGetAccessible(null));
        Assert.True(McpToolAccessMeta.IsAccessibleOrDefault(null));
        Assert.True(McpToolAccessMeta.IsAccessibleOrDefault(new JsonObject()));
    }

    [Fact]
    public void TryGetAccessible_rejects_non_boolean()
    {
        var meta = new JsonObject { [McpToolAccessMeta.Key] = "yes" };

        Assert.Null(McpToolAccessMeta.TryGetAccessible(meta));
        Assert.True(McpToolAccessMeta.IsAccessibleOrDefault(meta));
    }

    [Fact]
    public void StampDescription_prefixes_inaccessible_only_once()
    {
        var once = McpToolAccessMeta.StampDescription("Reads a report.", accessible: false);
        Assert.StartsWith(McpToolAccessMeta.InaccessibleDescriptionPrefix, once, StringComparison.Ordinal);
        Assert.EndsWith("Reads a report.", once, StringComparison.Ordinal);

        var twice = McpToolAccessMeta.StampDescription(once, accessible: false);
        Assert.Equal(once, twice);

        Assert.Equal("Reads a report.", McpToolAccessMeta.StampDescription("Reads a report.", accessible: true));
    }
}
