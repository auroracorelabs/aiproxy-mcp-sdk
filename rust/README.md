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
(`message_signed`); never derive it from tool arguments.

License: MIT.
