# Changelog

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
