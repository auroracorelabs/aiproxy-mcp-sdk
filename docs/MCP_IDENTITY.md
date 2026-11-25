# MCP identity: wire vs connection manifest vs SDK

Aurora MCP servers expose **two** discovery surfaces. Do not put catalogue branding on the
connection manifest.

## Field matrix

| Concern | Source of truth | Surface |
|---------|-----------------|---------|
| Programmatic server id | MCP `Implementation.name` / ABI `serverName` | `initialize` |
| UI display name | MCP `Implementation.title` (ABI `title`) | `initialize` |
| Human description | MCP `Implementation.description` (ABI `description`) | `initialize` |
| Icon | MCP `Implementation.icons[]` (ABI `icons`; use `icon_svg_to_mcp_icons`) | `initialize` |
| Model usage hints | MCP `instructions` (ABI `instructions`) | `initialize` |
| Setup auth mode | `auth.kind` (`none` / `instance_config` / `platform_oauth` / `mcp_oauth`) | connection manifest |
| Platform OAuth / inject / derived | `oauth`, `inject`, `derived`, `platformPrerequisites` | connection manifest |
| Instance config schema | `configParameters` (unified into `instanceConfig` when empty) | ABI + manifest |
| Transport AiProxy→MCP | `transportAuth` (`message_signed`) — **host-stamped only** | connection manifest |
| Product id / portable / integration type | `identity.id`, `portable`, `integrationType` | connection manifest |
| Supervisor catalogue row | `plugin.json` — generate via `plugin_metadata_json` | disk next to `.so` |

## Removed from the manifest

- **`identity.iconSvg`** — branding is MCP `icons` only.
- **`identity.displayName` as catalogue title** — still allowed as a setup name hint; AiProxy
  catalogue / wizard branding reads `Implementation.title` from `initialize`.

## Author checklist (SDK)

1. Set `title`, `description`, `icons` on `PluginDescriptor` (Rust) or `ServerInfo` (.NET).
2. Put AiProxy setup only on `connection` (`auth`, `inject`, …).
3. Declare config once in `config_parameters`; the ABI export fills `instanceConfig` when empty.
4. Emit `plugin.json` with `plugin_metadata_json` (or keep it in sync with `description` / `title`).
5. Use `message_signed` via host / `UseMessageSignature`; never invent transport auth in the plugin.
6. Stamp per-caller tool access with `_meta.accessible` when authorization differs by user.

See also mcphost `docs/MCP_CONNECTION_MANIFEST.md` and [MESSAGE_SIGNED.md](MESSAGE_SIGNED.md).
