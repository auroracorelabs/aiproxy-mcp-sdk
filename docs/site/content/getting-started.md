# Getting started

Install a package, sign or verify MCP HTTP traffic with `message_signed`, then optionally ship a native Rust plugin.

## Install the SDK

| SDK | Command |
| --- | --- |
| .NET (client / any host) | `dotnet add package AiProxy.Mcp.Sdk` |
| .NET (ASP.NET Core server) | `dotnet add package AiProxy.Mcp.Sdk.AspNetCore` |
| Rust | `cargo add aiproxy-mcp-sdk` |

Language details: [.NET](/dotnet/) and [Rust](/rust/). Packaging and tags: [Build and release](/releasing/).

## First ASP.NET Core MCP endpoint

Create a trusted-signers directory (fail closed until a key is installed), register verification, and scope middleware to the MCP path:

```csharp
builder.Services.AddMessageSignature(
    MessageSignatureOptions.LoadRequiredOrInitialize("example-mcp", "data/trusted-signers"));
app.UseMessageSignature("/mcp");   // HttpContext.User = signed caller, claim "username"
app.MapMcp("/mcp");
```

Clients sign every request with an ES256 key and assert the acting user. Install the public key as `trusted-signers/<kid>.pem` where the stem equals `McpSigningKey.ComputeKeyId`.

```csharp
using var key = McpSigningKey.LoadOrCreate("mcp-signing-key.pem");
var http = new HttpClient(new McpSigningHandler(
    new McpMessageSigner(key), "example-mcp", caller, new HttpClientHandler()));
```

Wire format, discovery, replay and `_meta.accessible`: [message_signed contract](/message-signed/).

## First Rust plugin

Depend on the crate, set `crate-type = ["cdylib"]`, implement `McpPlugin`, and export with `export_mcp_plugin!`. Identity in `CallContext` comes only from a host-verified signature — never from tool arguments. See the [Rust guide](/rust/).

## Development

For SDK contributors, see [building and releasing](/releasing/) and [documentation maintenance](/documentation/). The repository [README](https://github.com/auroracorelabs/aiproxy-mcp-sdk#readme) remains a standalone introduction for GitHub visitors.
