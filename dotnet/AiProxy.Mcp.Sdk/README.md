# AiProxy.Mcp.Sdk

`message_signed` transport authentication for MCP over HTTP. Targets **netstandard2.0** (.NET
Framework 4.6.1+, Mono, Unity, every .NET Core / .NET) and **net8.0** (no extra dependencies,
trimming and Native AOT compatible).

- **Client** (calls MCP servers): `McpMessageSigner` + `McpSigningHandler` sign every request with an
  ES256 key and assert the acting user (`sub`, `username`, `name`, `email`, `groups`, `roles`).
- **Server**: `MessageSignatureVerifier` checks trusted keys, audience, time window,
  method/path/body binding and single-use `jti`, and returns the signed caller. Apply your own
  permissions to `username`. ASP.NET Core hosts use the middleware in
  [AiProxy.Mcp.Sdk.AspNetCore](https://www.nuget.org/packages/AiProxy.Mcp.Sdk.AspNetCore).

```csharp
using var key = McpSigningKey.LoadOrCreate("mcp-signing-key.pem");
var http = new HttpClient(new McpSigningHandler(
    new McpMessageSigner(key), "my-mcp", caller, new HttpClientHandler()));
```

On netstandard2.0 under Unix, `McpSigningKey.LoadOrCreate` only loads an existing key: the runtime
cannot create the file with mode 0600, so create it with `openssl genpkey -algorithm EC -pkeyopt
ec_paramgen_curve:P-256` and `chmod 600`.

License: MIT.
