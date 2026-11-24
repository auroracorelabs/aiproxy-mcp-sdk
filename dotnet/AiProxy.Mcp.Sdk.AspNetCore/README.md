# AiProxy.Mcp.Sdk.AspNetCore

ASP.NET Core middleware for `message_signed` MCP endpoints (.NET 8+). Verifies `Mcp-Message-Signature`
with [AiProxy.Mcp.Sdk](https://www.nuget.org/packages/AiProxy.Mcp.Sdk) and sets `HttpContext.User`
to the signed caller (claims `username`, `name`, `email`, `group`, `role`). Apply your own
permissions to `username`.

```csharp
builder.Services.AddMessageSignature(
    MessageSignatureOptions.LoadRequiredOrInitialize("my-mcp", "data/trusted-signers"));
app.UseMessageSignature("/mcp");   // only /mcp is verified; other routes are untouched
app.MapMcp("/mcp");
```

License: MIT.
