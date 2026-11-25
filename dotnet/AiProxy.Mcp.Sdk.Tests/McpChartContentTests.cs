using System;
using System.Text.Json;
using AiProxy.Mcp.Sdk.Charts;
using Xunit;

namespace AiProxy.Mcp.Sdk.Tests;

public sealed class McpChartContentTests
{
    [Fact]
    public void CreateToolResultJson_Bar_EmitsTextAndResourceWithSnakeCaseBody()
    {
        var spec = new ChartBarSpec(
            "Tickets by severity",
            new[]
            {
                new ChartBar("Low", 12),
                new ChartBar("High", 3),
            },
            title: "Tickets",
            seriesLabel: "Tickets");

        var json = McpChartContent.CreateToolResultJson(spec, "12 low, 3 high.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("isError", out _));
        Assert.Equal("text", root.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("12 low, 3 high.", root.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("resource", root.GetProperty("content")[1].GetProperty("type").GetString());
        var resource = root.GetProperty("content")[1].GetProperty("resource");
        Assert.Equal(McpChartContent.MimeType, resource.GetProperty("mimeType").GetString());
        Assert.StartsWith("aiproxy://chart/", resource.GetProperty("uri").GetString(), StringComparison.Ordinal);

        using var body = JsonDocument.Parse(resource.GetProperty("text").GetString()!);
        Assert.Equal("bar", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal("Tickets by severity", body.RootElement.GetProperty("aria_label").GetString());
        Assert.Equal("Tickets", body.RootElement.GetProperty("series_label").GetString());
        Assert.Equal(12, body.RootElement.GetProperty("bars")[0].GetProperty("value").GetDouble());
    }

    [Fact]
    public void CreateToolResultJson_Line_AllowsNullGaps()
    {
        var spec = new ChartLineSpec(
            "Monthly KPI",
            new[] { "Jan", "Feb" },
            new[]
            {
                new ChartSeries("main", "Value", new double?[] { 42, null }),
            },
            title: "Monthly KPI",
            referenceLines: new[] { new ChartReferenceLine(50, "Target") },
            includeZero: true);

        var json = McpChartContent.CreateToolResultJson(spec, "Peaked later.");
        using var doc = JsonDocument.Parse(json);
        var bodyText = doc.RootElement.GetProperty("content")[1].GetProperty("resource").GetProperty("text").GetString();
        using var body = JsonDocument.Parse(bodyText!);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("series")[0].GetProperty("values")[1].ValueKind);
        Assert.True(body.RootElement.GetProperty("include_zero").GetBoolean());
        Assert.Equal(50, body.RootElement.GetProperty("reference_lines")[0].GetProperty("value").GetDouble());
    }

    [Fact]
    public void CreateToolResultJson_Gauge_RejectsOutOfRange()
    {
        var spec = new ChartGaugeSpec("g", value: 150, min: 0, max: 100);
        Assert.Throws<ArgumentException>(() => McpChartContent.CreateToolResultJson(spec, "bad"));
    }

    [Fact]
    public void CreateToolResultJson_RejectsEmptySummary()
    {
        var spec = new ChartBarSpec("a", new[] { new ChartBar("x", 1) });
        Assert.Throws<ArgumentException>(() => McpChartContent.CreateToolResultJson(spec, "  "));
    }

    [Fact]
    public void ChartKindNames_RejectsUnknown()
    {
        Assert.False(ChartKindNames.TryParse("pie", out _));
        Assert.True(ChartKindNames.TryParse("donut", out var kind));
        Assert.Equal(ChartKind.Donut, kind);
    }
}
