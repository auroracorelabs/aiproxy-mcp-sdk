# Changelog

## 1.0.3 — 2026-11-25

- **MCP wire identity on the plugin descriptor.** Rust `PluginDescriptor` gains `title`,
  `description`, and `icons` (MCP `Implementation` on `initialize`). Helper
  `icon_svg_to_mcp_icons` builds a data-URI icon from inline SVG. `plugin_metadata_json` derives
  `plugin.json` fields so authors do not invent a second description.
  `PluginDescriptor::unify_instance_config` (also applied in `export_mcp_plugin!`) copies
  `configParameters` into `connection.instanceConfig` when empty.
- **Breaking (ABI):** `connection.identity.iconSvg` removed — branding is MCP `icons` only. Field
  matrix: `docs/MCP_IDENTITY.md`.
- **.NET:** typed connection-manifest models, `McpIconHelper`, and
  `MapMcpConnectionManifest` for non-plugin HTTP MCP hosts (e.g. Compass).

## 1.0.2 — 2026-11-25

- **SDK documentation site.** Custom static site under `docs/site/`: guides from package READMEs and
  `docs/MESSAGE_SIGNED.md`, DocFX + rustdoc API references, GitHub Pages workflow
  (`.github/workflows/docs.yml`). Local preview on port 8092. Setup and known gaps (Pages
  enablement, no product docs domain, no runnable examples yet): `docs/PAGES.md`.

## 1.0.1 — 2026-11-25

- **Per-caller tool accessibility on `tools/list`.** Servers that authorize differently per signed
  user stamp each listed tool with `_meta.accessible` (`true`/`false`) via
  `McpToolAccessMeta.SetAccessible`. Clients read it with `TryGetAccessible` /
  `IsAccessibleOrDefault` (missing key ⇒ accessible, for legacy hosts). Also ships
  `InaccessibleDescriptionPrefix` and `InaccessibleInvokeMessage` so gateways can label and deny
  inaccessible tools consistently. Documented in `docs/MESSAGE_SIGNED.md`.

## 1.0.0 — 2026-11-24

- `message_signed` ES256 signer (`McpMessageSigner`, `McpSigningHandler`) and verifier
  (`MessageSignatureVerifier`) on netstandard2.0 / net8.0; ASP.NET Core middleware
  `UseMessageSignature`.
- `iss` is required; clients set it via `McpMessageSigner` (default `aiproxy`); servers may pin
  `ExpectedIssuer` / `MessageSignatureIssuer`.
- Trusted-signers `<kid>.pem` stem must equal `McpSigningKey.ComputeKeyId` (SPKI hash prefix).
- Replay cache is in-process only — single verifying process per audience (documented).
- Auth HTTP JSON body is generic (`unauthorized`); detailed reasons stay in server logs.
- Private key create uses Unix mode 0600 at open (no chmod race).
- Rust `CallContext::has_user` treats non-empty `username` as a user; plugins that authorize must
  override `call_tool_with_context` (or set `requires_user_context`).
