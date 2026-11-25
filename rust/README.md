# aiproxy-mcp-sdk

Rust SDK for native (C ABI) MCP server plugins: typed descriptor / tool / result shapes, the
verified caller (`CallContext`: `user_id`, `username`, `name`, `email`, `groups`, `roles`) and the
`export_mcp_plugin!` macro that generates the `mcp_plugin_*` exports.

```toml
[lib]
crate-type = ["cdylib"]

[dependencies]
aiproxy-mcp-sdk = "1"
```

Identity in `CallContext` comes only from a request signature the host has verified
(`message_signed`); never derive it from tool arguments. `CallContext::has_user` is true when
`user_id` and/or `username` is present; MCP-side authorization still keys off `username`.

Override `call_tool_with_context` (or set `requires_user_context`) when the plugin must see the
caller — the default implementation ignores context and calls `call_tool`.

Per-caller tool accessibility on `tools/list` uses `_meta.accessible` (`TOOL_ACCESS_META_KEY`);
see `docs/MESSAGE_SIGNED.md`.

**Branding** (`title`, `description`, `icons`) belongs on `PluginDescriptor` / MCP `initialize`,
not on the connection manifest. Use `icon_svg_to_mcp_icons` for SVG. Setup auth / inject stay on
`connection`. Field matrix: `docs/MCP_IDENTITY.md`.

**Charts:** return `CallToolResult::chart(spec, summary)` so AiProxy / Aurora.AI can render
declarative chart JSON (`docs/CHARTS.md`). Always include the text summary for the LLM.

License: MIT.
