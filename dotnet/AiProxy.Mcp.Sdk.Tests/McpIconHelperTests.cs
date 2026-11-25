using AiProxy.Mcp.Sdk.ConnectionManifest;

namespace AiProxy.Mcp.Sdk.Tests;

public sealed class McpIconHelperTests
{
    [Fact]
    public void FromInlineSvg_blank_returns_empty()
    {
        Assert.Empty(McpIconHelper.FromInlineSvg(null));
        Assert.Empty(McpIconHelper.FromInlineSvg("  "));
    }

    [Fact]
    public void FromInlineSvg_round_trips_through_TryExtractInlineSvg()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"></svg>";
        var icons = McpIconHelper.FromInlineSvg(svg);
        Assert.Single(icons);
        Assert.Equal("image/svg+xml", icons[0].MimeType);
        Assert.Equal(["any"], icons[0].Sizes);
        Assert.StartsWith("data:image/svg+xml,", icons[0].Src, StringComparison.Ordinal);

        var extracted = McpIconHelper.TryExtractInlineSvg(icons[0].Src);
        Assert.Equal(svg, extracted);
    }

    [Fact]
    public void TryExtractInlineSvg_rejects_non_svg_data_uri()
    {
        Assert.Null(McpIconHelper.TryExtractInlineSvg("https://example.test/icon.png"));
        Assert.Null(McpIconHelper.TryExtractInlineSvg("data:image/png;base64,aaaa"));
    }
}
