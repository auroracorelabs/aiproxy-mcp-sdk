# aiproxy-mcp-sdk

The single SDK for MCP servers and their clients: everything needed to **build** an MCP plugin and to
**use** (call or host) one.

| Path | Package | Use for |
|------|---------|---------|
| `dotnet/AiProxy.Mcp.Sdk` | NuGet [`AiProxy.Mcp.Sdk`](https://www.nuget.org/packages/AiProxy.Mcp.Sdk) — **netstandard2.0** + net8.0 | `message_signed` signer (`McpMessageSigner`, `McpSigningHandler`) and verifier (`MessageSignatureVerifier`): .NET Framework 4.7.2+, Mono, Unity, every .NET |
| `dotnet/AiProxy.Mcp.Sdk.AspNetCore` | NuGet [`AiProxy.Mcp.Sdk.AspNetCore`](https://www.nuget.org/packages/AiProxy.Mcp.Sdk.AspNetCore) — net8.0 | ASP.NET Core middleware `UseMessageSignature` for MCP servers on ASP.NET Core |
| `rust` | crate [`aiproxy-mcp-sdk`](https://crates.io/crates/aiproxy-mcp-sdk) | Native (C ABI) plugins for McpServerHost: descriptor/tool types, `CallContext`, `export_mcp_plugin!` |

## message_signed in one paragraph

The client signs every HTTP request with its ES256 key (`Mcp-Message-Signature`, compact JWS bound to
method, path, body hash, audience and a single-use `jti`) and asserts the acting user: `sub`,
`username`, `name`, `email`, `groups`, `roles`. The server trusts only public keys in its
trusted-signers directory (`<kid>.pem`) and applies **its own** permissions to `username`. Wire
format: [docs/MESSAGE_SIGNED.md](docs/MESSAGE_SIGNED.md).

```csharp
// MCP endpoint inside an existing ASP.NET app: directory created on first start, fail closed until a key is installed
builder.Services.AddMessageSignature(MessageSignatureOptions.LoadRequiredOrInitialize("example-mcp", "data/trusted-signers"));
app.UseMessageSignature("/mcp");   // HttpContext.User = signed caller, claim "username"
```

## Consuming

- .NET: `AiProxy.Mcp.Sdk` (clients and servers, any runtime) or `AiProxy.Mcp.Sdk.AspNetCore`
  (ASP.NET Core servers).
- Rust: `aiproxy-mcp-sdk = "1"`.

## Build and test

```
dotnet test aiproxy-mcp-sdk.slnx
dotnet test dotnet/AiProxy.Mcp.Sdk.Tests -p:SdkTargetFramework=netstandard2.0   # the netstandard2.0 build
cargo test --workspace --manifest-path rust/Cargo.toml
```

## Release (GitHub Actions)

- `.github/workflows/ci.yml`: tests (net8.0 and netstandard2.0 builds), `dotnet pack`, a .NET Framework
  4.7.2 consumer run on Windows (`dotnet/compatibility`), clippy and `cargo publish --dry-run` on push/PR.
- `.github/workflows/release.yml`: tag `vX.Y.Z` publishes both packages as `X.Y.Z` (manual re-run per
  registry via *Run workflow* on the tag).
  1. Set `VERSION` and `version` in `rust/Cargo.toml` to `X.Y.Z` (the workflow fails if they differ from the tag).
  2. GitHub environment `sdk-release`: variable `NUGET_USER` (nuget.org trusted publishing policy for
     this repository) and secret `CARGO_REGISTRY_TOKEN` (crates.io, publish for `aiproxy-mcp-sdk`).
  3. `git tag vX.Y.Z && git push origin vX.Y.Z`.

## License

[MIT](LICENSE) — free for personal and commercial use.
