using System.Security.Cryptography;

namespace AiProxy.Mcp.Sdk.Internal;

internal static class Sha256
{
    public static byte[] Hash(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }
}
