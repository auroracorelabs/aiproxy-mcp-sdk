using System;
using System.Collections.Generic;

namespace AiProxy.Mcp.Sdk.Charts;

/// <summary>One series on a line chart.</summary>
public sealed class ChartSeries
{
    public ChartSeries(string id, string label, IReadOnlyList<double?> values)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public string Id { get; }
    public string Label { get; }
    public IReadOnlyList<double?> Values { get; }
}

/// <summary>Horizontal threshold on a line chart.</summary>
public sealed class ChartReferenceLine
{
    public ChartReferenceLine(double value, string? label = null)
    {
        Value = value;
        Label = label;
    }

    public double Value { get; }
    public string? Label { get; }
}

/// <summary>One bar on a bar chart.</summary>
public sealed class ChartBar
{
    public ChartBar(string label, double value)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Value = value;
    }

    public string Label { get; }
    public double Value { get; }
}

/// <summary>One slice on a donut chart.</summary>
public sealed class ChartSlice
{
    public ChartSlice(string id, string label, double value)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Value = value;
    }

    public string Id { get; }
    public string Label { get; }
    public double Value { get; }
}

/// <summary>Line chart: shared labels and one or more series.</summary>
public sealed class ChartLineSpec
{
    public ChartLineSpec(
        string ariaLabel,
        IReadOnlyList<string> labels,
        IReadOnlyList<ChartSeries> series,
        string? title = null,
        IReadOnlyList<ChartReferenceLine>? referenceLines = null,
        bool? includeZero = null)
    {
        AriaLabel = ariaLabel ?? throw new ArgumentNullException(nameof(ariaLabel));
        Labels = labels ?? throw new ArgumentNullException(nameof(labels));
        Series = series ?? throw new ArgumentNullException(nameof(series));
        Title = title;
        ReferenceLines = referenceLines;
        IncludeZero = includeZero;
    }

    public string AriaLabel { get; }
    public IReadOnlyList<string> Labels { get; }
    public IReadOnlyList<ChartSeries> Series { get; }
    public string? Title { get; }
    public IReadOnlyList<ChartReferenceLine>? ReferenceLines { get; }
    public bool? IncludeZero { get; }
}

/// <summary>Bar chart: discrete labelled bars.</summary>
public sealed class ChartBarSpec
{
    public ChartBarSpec(
        string ariaLabel,
        IReadOnlyList<ChartBar> bars,
        string? title = null,
        string? seriesLabel = null)
    {
        AriaLabel = ariaLabel ?? throw new ArgumentNullException(nameof(ariaLabel));
        Bars = bars ?? throw new ArgumentNullException(nameof(bars));
        Title = title;
        SeriesLabel = seriesLabel;
    }

    public string AriaLabel { get; }
    public IReadOnlyList<ChartBar> Bars { get; }
    public string? Title { get; }
    public string? SeriesLabel { get; }
}

/// <summary>Donut chart: part-to-whole slices.</summary>
public sealed class ChartDonutSpec
{
    public ChartDonutSpec(
        string ariaLabel,
        IReadOnlyList<ChartSlice> slices,
        string? title = null,
        double? total = null)
    {
        AriaLabel = ariaLabel ?? throw new ArgumentNullException(nameof(ariaLabel));
        Slices = slices ?? throw new ArgumentNullException(nameof(slices));
        Title = title;
        Total = total;
    }

    public string AriaLabel { get; }
    public IReadOnlyList<ChartSlice> Slices { get; }
    public string? Title { get; }
    public double? Total { get; }
}

/// <summary>Gauge chart: single value between min and max.</summary>
public sealed class ChartGaugeSpec
{
    public ChartGaugeSpec(
        string ariaLabel,
        double value,
        double min,
        double max,
        string? title = null,
        IReadOnlyList<double>? points = null)
    {
        AriaLabel = ariaLabel ?? throw new ArgumentNullException(nameof(ariaLabel));
        Value = value;
        Min = min;
        Max = max;
        Title = title;
        Points = points;
    }

    public string AriaLabel { get; }
    public double Value { get; }
    public double Min { get; }
    public double Max { get; }
    public string? Title { get; }
    public IReadOnlyList<double>? Points { get; }
}
