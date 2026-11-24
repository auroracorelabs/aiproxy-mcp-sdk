using System.Runtime.CompilerServices;

namespace AiProxy.Mcp.Sdk.Internal;

/// <summary>Length-then-content comparison that does not short-circuit on content (CryptographicOperations is not in netstandard2.0).</summary>
internal static class ConstantTime
{
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool Equals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        var diff = 0;
        for (var i = 0; i < left.Length; i++)
            diff |= left[i] ^ right[i];
        return diff == 0;
    }
}
