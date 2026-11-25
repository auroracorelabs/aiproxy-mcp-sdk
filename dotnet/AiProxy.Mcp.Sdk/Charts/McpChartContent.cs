using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AiProxy.Mcp.Sdk.Charts;

/// <summary>
/// Builds MCP <c>CallToolResult</c> JSON with a text summary and an embedded chart resource
/// (<c>application/vnd.aiproxy.chart+json</c>). See <c>docs/CHARTS.md</c>.
/// </summary>
public static class McpChartContent
{
    /// <summary>MIME type for chart JSON in an MCP embedded resource.</summary>
    public const string MimeType = "application/vnd.aiproxy.chart+json";

    private static long _chartId;

    /// <summary>Validates and serializes a line chart into CallToolResult JSON.</summary>
    public static string CreateToolResultJson(ChartLineSpec spec, string summary)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateLine(spec);
        return BuildResult(summary, WriteLineBody(spec));
    }

    /// <summary>Validates and serializes a bar chart into CallToolResult JSON.</summary>
    public static string CreateToolResultJson(ChartBarSpec spec, string summary)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateBar(spec);
        return BuildResult(summary, WriteBarBody(spec));
    }

    /// <summary>Validates and serializes a donut chart into CallToolResult JSON.</summary>
    public static string CreateToolResultJson(ChartDonutSpec spec, string summary)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateDonut(spec);
        return BuildResult(summary, WriteDonutBody(spec));
    }

    /// <summary>Validates and serializes a gauge chart into CallToolResult JSON.</summary>
    public static string CreateToolResultJson(ChartGaugeSpec spec, string summary)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateGauge(spec);
        return BuildResult(summary, WriteGaugeBody(spec));
    }

    /// <summary>True when <paramref name="mimeType"/> is the AiProxy chart MIME.</summary>
    public static bool IsChartMimeType(string? mimeType) =>
        string.Equals(mimeType, MimeType, StringComparison.Ordinal);

    static string BuildResult(string summary, string chartJson)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("A text summary is required alongside the chart.", nameof(summary));

        var id = Interlocked.Increment(ref _chartId);
        var uri = "aiproxy://chart/" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x", CultureInfo.InvariantCulture)
            + "-" + id.ToString(CultureInfo.InvariantCulture);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("content");
            writer.WriteStartArray();

            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", summary);
            writer.WriteEndObject();

            writer.WriteStartObject();
            writer.WriteString("type", "resource");
            writer.WritePropertyName("resource");
            writer.WriteStartObject();
            writer.WriteString("uri", uri);
            writer.WriteString("mimeType", MimeType);
            writer.WriteString("text", chartJson);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static void RequireAria(string ariaLabel)
    {
        if (string.IsNullOrWhiteSpace(ariaLabel))
            throw new ArgumentException("aria_label is required.");
    }

    static void ValidateLine(ChartLineSpec spec)
    {
        RequireAria(spec.AriaLabel);
        if (spec.Series.Count == 0)
            throw new ArgumentException("line chart requires at least one series.");
        foreach (var series in spec.Series)
        {
            if (string.IsNullOrWhiteSpace(series.Id) || string.IsNullOrWhiteSpace(series.Label))
                throw new ArgumentException("line series id and label are required.");
            if (series.Values.Count != spec.Labels.Count)
                throw new ArgumentException(
                    "line series '" + series.Id + "' values length must match labels.");
        }
    }

    static void ValidateBar(ChartBarSpec spec)
    {
        RequireAria(spec.AriaLabel);
        if (spec.Bars.Count == 0)
            throw new ArgumentException("bar chart requires at least one bar.");
    }

    static void ValidateDonut(ChartDonutSpec spec)
    {
        RequireAria(spec.AriaLabel);
        if (spec.Slices.Count == 0)
            throw new ArgumentException("donut chart requires at least one slice.");
        foreach (var slice in spec.Slices)
        {
            if (string.IsNullOrWhiteSpace(slice.Id) || string.IsNullOrWhiteSpace(slice.Label))
                throw new ArgumentException("donut slice id and label are required.");
        }

        if (spec.Total is { } total && (double.IsNaN(total) || double.IsInfinity(total) || total < 0))
            throw new ArgumentException("donut total must be a non-negative finite number.");
    }

    static void ValidateGauge(ChartGaugeSpec spec)
    {
        RequireAria(spec.AriaLabel);
        if (double.IsNaN(spec.Min) || double.IsInfinity(spec.Min)
            || double.IsNaN(spec.Max) || double.IsInfinity(spec.Max)
            || double.IsNaN(spec.Value) || double.IsInfinity(spec.Value))
            throw new ArgumentException("gauge min, max, and value must be finite.");
        if (!(spec.Min < spec.Max))
            throw new ArgumentException("gauge min must be less than max.");
        if (spec.Value < spec.Min || spec.Value > spec.Max)
            throw new ArgumentException(
                "gauge value " + spec.Value.ToString(CultureInfo.InvariantCulture)
                + " is outside [" + spec.Min.ToString(CultureInfo.InvariantCulture)
                + ", " + spec.Max.ToString(CultureInfo.InvariantCulture) + "].");
    }

    static string WriteLineBody(ChartLineSpec spec)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", ChartKindNames.Line);
            WriteOptionalTitle(writer, spec.Title);
            writer.WriteString("aria_label", spec.AriaLabel);
            writer.WritePropertyName("labels");
            writer.WriteStartArray();
            foreach (var label in spec.Labels)
                writer.WriteStringValue(label);
            writer.WriteEndArray();
            writer.WritePropertyName("series");
            writer.WriteStartArray();
            foreach (var series in spec.Series)
            {
                writer.WriteStartObject();
                writer.WriteString("id", series.Id);
                writer.WriteString("label", series.Label);
                writer.WritePropertyName("values");
                writer.WriteStartArray();
                foreach (var value in series.Values)
                {
                    if (value is null) writer.WriteNullValue();
                    else writer.WriteNumberValue(value.Value);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (spec.ReferenceLines is { Count: > 0 } refs)
            {
                writer.WritePropertyName("reference_lines");
                writer.WriteStartArray();
                foreach (var line in refs)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("value", line.Value);
                    if (!string.IsNullOrWhiteSpace(line.Label))
                        writer.WriteString("label", line.Label);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            if (spec.IncludeZero is { } includeZero)
                writer.WriteBoolean("include_zero", includeZero);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static string WriteBarBody(ChartBarSpec spec)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", ChartKindNames.Bar);
            WriteOptionalTitle(writer, spec.Title);
            writer.WriteString("aria_label", spec.AriaLabel);
            writer.WritePropertyName("bars");
            writer.WriteStartArray();
            foreach (var bar in spec.Bars)
            {
                writer.WriteStartObject();
                writer.WriteString("label", bar.Label);
                writer.WriteNumber("value", bar.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (!string.IsNullOrWhiteSpace(spec.SeriesLabel))
                writer.WriteString("series_label", spec.SeriesLabel);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static string WriteDonutBody(ChartDonutSpec spec)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", ChartKindNames.Donut);
            WriteOptionalTitle(writer, spec.Title);
            writer.WriteString("aria_label", spec.AriaLabel);
            writer.WritePropertyName("slices");
            writer.WriteStartArray();
            foreach (var slice in spec.Slices)
            {
                writer.WriteStartObject();
                writer.WriteString("id", slice.Id);
                writer.WriteString("label", slice.Label);
                writer.WriteNumber("value", slice.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (spec.Total is { } total)
                writer.WriteNumber("total", total);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static string WriteGaugeBody(ChartGaugeSpec spec)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", ChartKindNames.Gauge);
            WriteOptionalTitle(writer, spec.Title);
            writer.WriteString("aria_label", spec.AriaLabel);
            writer.WriteNumber("value", spec.Value);
            writer.WriteNumber("min", spec.Min);
            writer.WriteNumber("max", spec.Max);
            if (spec.Points is { Count: > 0 } points)
            {
                writer.WritePropertyName("points");
                writer.WriteStartArray();
                foreach (var point in points)
                    writer.WriteNumberValue(point);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteOptionalTitle(Utf8JsonWriter writer, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
            writer.WriteString("title", title);
    }
}
