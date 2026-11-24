namespace AiProxy.Mcp.Sdk.Internal;

/// <summary>Argument checks available on every target (the BCL throw helpers are .NET 6+).</summary>
internal static class Guard
{
    public static T NotNull<T>(T? value, string name)
        where T : class =>
        value ?? throw new ArgumentNullException(name);

    public static string NotNullOrEmpty(string? value, string name)
    {
        if (value is null)
            throw new ArgumentNullException(name);
        if (value.Length == 0)
            throw new ArgumentException("Value cannot be empty.", name);
        return value;
    }

    public static string NotNullOrWhiteSpace(string? value, string name)
    {
        if (value is null)
            throw new ArgumentNullException(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty or whitespace.", name);
        return value;
    }
}
