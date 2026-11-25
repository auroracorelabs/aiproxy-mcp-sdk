# AiProxy MCP SDK

Build MCP servers and plugins with shared `message_signed` transport authentication. Choose .NET for HTTP clients and ASP.NET Core hosts, or Rust for native (C ABI) plugins loaded by a plugin host via `dlopen` / `NativeLibrary.Load`.

## Choose your SDK

| SDK | Integration guide | Generated API reference |
| --- | --- | --- |
| .NET (`AiProxy.Mcp.Sdk` + AspNetCore) | [.NET](/dotnet/) | [.NET API](/reference/dotnet/index.html) |
| Rust (`aiproxy-mcp-sdk`) | [Rust](/rust/) | [Rust API](/reference/rust/aiproxy_mcp_sdk/index.html) |

## Start integrating

1. [Install the SDK](/getting-started/#install-the-sdk) from NuGet or crates.io.
2. Wire `message_signed` on the client (`McpMessageSigner` / `McpSigningHandler`) or the ASP.NET Core host (`UseMessageSignature`).
3. For native plugins, implement `McpPlugin` and export with `export_mcp_plugin!`.
4. When access differs by signed user, stamp `tools/list` with `_meta.accessible` via `McpToolAccessMeta`.

Use [Getting started](/getting-started/) for install and first wiring. The [message_signed contract](/message-signed/) is the wire specification.

## API reference

API references are generated in the documentation build: DocFX for .NET and rustdoc for Rust. They cover public types, options and method signatures.

This site documents the SDK checkout deployed from the repository's default branch. The shared SDK version is shown in the navigation. Match your installed package to the corresponding source version when inspecting release-specific behavior.

## Source and packages

- [SDK repository on GitHub](https://github.com/auroracorelabs/aiproxy-mcp-sdk)
- [NuGet AiProxy.Mcp.Sdk](https://www.nuget.org/packages/AiProxy.Mcp.Sdk) · [AiProxy.Mcp.Sdk.AspNetCore](https://www.nuget.org/packages/AiProxy.Mcp.Sdk.AspNetCore)
- [crates.io aiproxy-mcp-sdk](https://crates.io/crates/aiproxy-mcp-sdk)
