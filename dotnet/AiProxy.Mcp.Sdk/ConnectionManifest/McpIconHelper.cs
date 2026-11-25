using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AiProxy.Mcp.Sdk.ConnectionManifest;

/// <summary>
/// Builds MCP <c>Icon</c> <c>src</c> data URIs from inline SVG so authors do not invent branding shapes.
/// Pair with MCP <c>Implementation.Icons</c> on <c>initialize</c> (not the connection manifest).
/// </summary>
public static class McpIconHelper
{
    /// <summary>
    /// Returns a single data-URI icon entry, or an empty list when <paramref name="svg"/> is blank.
    /// </summary>
    public static IReadOnlyList<McpIconSpec> FromInlineSvg(string? svg)
    {
        if (string.IsNullOrWhiteSpace(svg))
            return Array.Empty<McpIconSpec>();

        var trimmed = svg!.Trim();
        var src = "data:image/svg+xml," + PercentEncodeUtf8(trimmed);
        return new[]
        {
            new McpIconSpec(src, "image/svg+xml", new[] { "any" }, null),
        };
    }

    /// <summary>
    /// Extracts inline SVG from a <c>data:image/svg+xml</c> URI (percent-encoded or base64).
    /// Returns null when the URI is not an SVG data URI.
    /// </summary>
    public static string? TryExtractInlineSvg(string? iconSrc)
    {
        if (string.IsNullOrWhiteSpace(iconSrc))
            return null;

        var src = iconSrc!.Trim();
        const string prefix = "data:image/svg+xml";
        if (src.Length < prefix.Length
            || !src.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var comma = src.IndexOf(',');
        if (comma < 0 || comma >= src.Length - 1)
            return null;

        var meta = src.Substring(0, comma);
        var payload = src.Substring(comma + 1);
        if (meta.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return Uri.UnescapeDataString(payload.Replace("+", "%2B"));
    }

    private static string PercentEncodeUtf8(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if ((b >= (byte)'A' && b <= (byte)'Z')
                || (b >= (byte)'a' && b <= (byte)'z')
                || (b >= (byte)'0' && b <= (byte)'9')
                || b is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%');
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }
}

/// <summary>MCP icon wire fields without depending on the ModelContextProtocol package.</summary>
public sealed class McpIconSpec
{
    public McpIconSpec(string src, string? mimeType, IReadOnlyList<string>? sizes, string? theme)
    {
        Src = src ?? throw new ArgumentNullException(nameof(src));
        MimeType = mimeType;
        Sizes = sizes;
        Theme = theme;
    }

    public string Src { get; }
    public string? MimeType { get; }
    public IReadOnlyList<string>? Sizes { get; }
    public string? Theme { get; }
}
