namespace AiProxy.Mcp.Sdk.Internal;

/// <summary>Unpadded base64url (RFC 4648 §5), as used by JWS. System.Buffers.Text.Base64Url is .NET 9+.</summary>
internal static class Base64UrlCodec
{
    public static string Encode(byte[] data)
    {
        Guard.NotNull(data, nameof(data));
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Strict decode: rejects padding, standard-alphabet characters and invalid lengths.</summary>
    public static bool TryDecode(string segment, out byte[] bytes)
    {
        bytes = [];
        if (segment is null || segment.Length % 4 == 1)
            return false;

        foreach (var c in segment)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                return false;
        }

        var base64 = segment.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };

        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
