# AiProxy.Mcp.Sdk.AspNetCore

ASP.NET Core middleware for `message_signed` MCP endpoints (.NET 8+). Verifies `Mcp-Message-Signature`
with [AiProxy.Mcp.Sdk](https://www.nuget.org/packages/AiProxy.Mcp.Sdk) and sets `HttpContext.User`
to the signed caller (claims `username`, `name`, `email`, `group`, `role`). Apply your own
permissions to `username`. Auth failures return a generic JSON body; details go to the log only.
Replay protection is in-process — run one verifying host per audience.

`htp` is `PathBase + Path + QueryString`: align `UsePathBase` / the reverse proxy with what the
client signs.

```csharp
builder.Services.AddMessageSignature(
    MessageSignatureOptions.LoadRequiredOrInitialize("my-mcp", "data/trusted-signers"));
app.UseMessageSignature("/mcp");   // only /mcp is verified; other routes are untouched
app.MapMcpConnectionManifest(new McpConnectionManifestDocument
{
    Identity = new() { Id = "my-mcp", DisplayName = "My MCP" },
    Auth = new() { Kind = "none" },
}); // stamps transportAuth from MessageSignatureOptions when enabled
app.MapMcp("/mcp");
```

Catalogue branding (`Implementation.title` / `description` / `icons`) belongs on MCP `initialize`,
not on the connection manifest — see `docs/MCP_IDENTITY.md` and `McpIconHelper`.

License: MIT.
