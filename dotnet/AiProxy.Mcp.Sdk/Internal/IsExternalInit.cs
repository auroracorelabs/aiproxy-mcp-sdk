#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>Enables <c>init</c> accessors and records when compiling for netstandard2.0.</summary>
internal static class IsExternalInit
{
}
#endif
