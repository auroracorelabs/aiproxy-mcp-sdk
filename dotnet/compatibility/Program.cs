using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>Signs with the SDK, sends through McpSigningHandler, verifies on the same runtime; exit code 0 = compatible.</summary>
internal static class Program
{
    private static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aiproxy-mcp-sdk-compat-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var keyPath = Path.Combine(dir, McpSigningKey.DefaultFileName);
            using var created = McpSigningKey.LoadOrCreate(keyPath);
            using var key = McpSigningKey.LoadOrCreate(keyPath);
            Check(created.KeyId == key.KeyId, "key file round trip");

            var signers = Path.Combine(dir, "trusted-signers");
            var options = MessageSignatureOptions.LoadRequiredOrInitialize("example-mcp", signers);
            Check(options.TrustedKeys.Count == 0, "empty trusted-signers directory created");
            File.WriteAllText(Path.Combine(signers, key.KeyId + ".pem"), key.PublicKeyPem);
            options = MessageSignatureOptions.LoadRequiredOrInitialize("example-mcp", signers);
            var verifier = new MessageSignatureVerifier(options, new JtiReplayCache());

            var caller = new McpCallerIdentity("u-1", "mrossi", "Mario Rossi", null, new[] { "Sales" }, new[] { McpCallerIdentity.RoleUser });
            var capture = new CaptureHandler();
            using (var http = new HttpClient(new McpSigningHandler(new McpMessageSigner(key), "example-mcp", caller, capture)))
            {
                http.PostAsync("http://localhost/mcp", new StringContent("{\"jsonrpc\":\"2.0\"}", Encoding.UTF8, "application/json"))
                    .GetAwaiter().GetResult().Dispose();
            }

            var result = verifier.Verify(capture.Signature, "POST", "/mcp", capture.Body);
            Check(result.Success, "signed request verifies: " + result.Error);
            Check(result.Identity!.Username == "mrossi", "username claim");
            Check(verifier.Verify(capture.Signature, "POST", "/mcp", capture.Body).Error == "replayed jti", "replay rejected");

            Console.WriteLine("AiProxy.Mcp.Sdk compatible on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED: " + ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("Check failed: " + what);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Signature { get; private set; }

        public byte[] Body { get; private set; } = new byte[0];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Signature = request.Headers.GetValues(McpMessageSigner.HeaderName).Single();
            Body = request.Content is null ? new byte[0] : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
