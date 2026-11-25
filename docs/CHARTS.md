# AiProxy MCP charts

Declarative chart JSON that MCP tools can return alongside text. AiProxy and other
Aurora.AI hosts render the payload as inline SVG. The chart body is product JSON
(**snake_case**). MCP protocol wrapper fields stay camelCase (`mimeType`, `isError`).

## MIME and wire shape

| Item | Value |
| --- | --- |
| MIME | `application/vnd.aiproxy.chart+json` |
| MCP block | `type: "resource"` with embedded `resource.uri`, `resource.mimeType`, `resource.text` |
| URI | `aiproxy://chart/{opaque-id}` (unique per block) |

Always emit a **text** content block (LLM / timeline summary) **and** the chart resource.
Never return a chart-only result.

Example `CallToolResult`:

```json
{
  "content": [
    {
      "type": "text",
      "text": "Monthly KPI peaked at 55 in February."
    },
    {
      "type": "resource",
      "resource": {
        "uri": "aiproxy://chart/01jexample",
        "mimeType": "application/vnd.aiproxy.chart+json",
        "text": "{\"kind\":\"line\",\"title\":\"Monthly KPI\",\"aria_label\":\"Monthly KPI\",\"labels\":[\"Jan\",\"Feb\"],\"series\":[{\"id\":\"main\",\"label\":\"Value\",\"values\":[42,55]}],\"reference_lines\":[{\"value\":50,\"label\":\"Target\"}]}"
      }
    }
  ]
}
```

## Closed `kind` set (v1)

| `kind` | Purpose |
| --- | --- |
| `line` | One or more series over shared `labels` |
| `bar` | Discrete labelled bars |
| `donut` | Part-to-whole slices |
| `gauge` | Single value between `min` and `max`, optional sparkline `points` |

Unknown `kind` values are rejected by SDK builders. Renderers tolerate bad third-party
payloads without throwing (empty / error state).

## Common fields

| Field | Required | Notes |
| --- | --- | --- |
| `kind` | yes | One of the literals above |
| `title` | no | Short heading shown above the chart |
| `aria_label` | yes | Accessible name for the SVG |

## `line`

| Field | Required | Notes |
| --- | --- | --- |
| `labels` | yes | X-axis labels; length must match each series `values` length |
| `series` | yes | Non-empty; each has `id`, `label`, `values` (`null` = gap) |
| `reference_lines` | no | `[{ "value", "label"? }]` |
| `include_zero` | no | Default `true` |

```json
{
  "kind": "line",
  "title": "Monthly KPI",
  "aria_label": "Monthly KPI",
  "labels": ["Jan", "Feb"],
  "series": [{ "id": "main", "label": "Value", "values": [42, 55] }],
  "reference_lines": [{ "value": 50, "label": "Target" }],
  "include_zero": true
}
```

## `bar`

| Field | Required | Notes |
| --- | --- | --- |
| `bars` | yes | Non-empty `[{ "label", "value" }]` |
| `series_label` | no | Measure name for tooltips |

```json
{
  "kind": "bar",
  "title": "Tickets by severity",
  "aria_label": "Tickets by severity",
  "bars": [
    { "label": "Low", "value": 12 },
    { "label": "High", "value": 3 }
  ],
  "series_label": "Tickets"
}
```

## `donut`

| Field | Required | Notes |
| --- | --- | --- |
| `slices` | yes | Non-empty `[{ "id", "label", "value" }]` |
| `total` | no | Whole the slices belong to; defaults to the sum. Larger than the sum leaves an empty track |

```json
{
  "kind": "donut",
  "title": "Capacity used",
  "aria_label": "Capacity used",
  "slices": [
    { "id": "used", "label": "Used", "value": 70 },
    { "id": "free", "label": "Free", "value": 30 }
  ],
  "total": 100
}
```

## `gauge`

| Field | Required | Notes |
| --- | --- | --- |
| `value` | yes | Current reading |
| `min` | yes | Lower bound (`min` < `max`) |
| `max` | yes | Upper bound |
| `points` | no | Optional sparkline values |

SDK builders **reject** `value` outside `[min, max]`. Host renderers **clamp** out-of-range
values from untrusted payloads.

```json
{
  "kind": "gauge",
  "title": "Monthly KPI",
  "aria_label": "Monthly KPI",
  "value": 42,
  "min": 0,
  "max": 100,
  "points": [10, 20, 42]
}
```

## SDK helpers

- **Rust:** `CallToolResult::chart(spec, summary)` in crate `aiproxy-mcp-sdk`
- **.NET:** `McpChartContent.CreateToolResultJson(spec, summary)` in `AiProxy.Mcp.Sdk`

## Host rendering

Aurora.AI ships `charts.js` / `charts.css` (`AuroraCharts.parse`, `AuroraCharts.render`,
`AuroraCharts.renderFromMcpContent`). AiProxy mounts charts from `tool_done` agent events that
carry extracted chart JSON. Do not use aurora.ui for this path.
