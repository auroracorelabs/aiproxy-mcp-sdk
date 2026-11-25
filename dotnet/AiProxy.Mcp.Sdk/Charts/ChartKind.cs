namespace AiProxy.Mcp.Sdk.Charts;

/// <summary>Closed chart kinds for <c>application/vnd.aiproxy.chart+json</c> (v1).</summary>
public enum ChartKind
{
    Line = 1,
    Bar = 2,
    Donut = 3,
    Gauge = 4,
}

/// <summary>Snake_case literals for <see cref="ChartKind"/>.</summary>
public static class ChartKindNames
{
    public const string Line = "line";
    public const string Bar = "bar";
    public const string Donut = "donut";
    public const string Gauge = "gauge";

    /// <summary>Returns the wire literal, or null when the value is not a known kind.</summary>
    public static string? ToWire(ChartKind kind) => kind switch
    {
        ChartKind.Line => Line,
        ChartKind.Bar => Bar,
        ChartKind.Donut => Donut,
        ChartKind.Gauge => Gauge,
        _ => null,
    };

    /// <summary>Parses a wire literal; returns false for unknown values.</summary>
    public static bool TryParse(string? value, out ChartKind kind)
    {
        switch (value)
        {
            case Line:
                kind = ChartKind.Line;
                return true;
            case Bar:
                kind = ChartKind.Bar;
                return true;
            case Donut:
                kind = ChartKind.Donut;
                return true;
            case Gauge:
                kind = ChartKind.Gauge;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
