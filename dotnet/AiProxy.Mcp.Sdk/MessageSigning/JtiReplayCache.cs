using System.Collections.Concurrent;
using AiProxy.Mcp.Sdk.Internal;

namespace AiProxy.Mcp.Sdk.MessageSigning;

/// <summary>
/// Remembers accepted <c>jti</c> values until they can no longer pass the time checks, so a captured
/// signed request cannot be replayed within this process.
/// <para>
/// <b>Single process only.</b> The cache is in-memory and is not shared across hosts, containers, or
/// worker processes. Do not run more than one verifying process for the same MCP audience without an
/// external shared replay store — otherwise a captured request can be replayed on another replica.
/// </para>
/// </summary>
public sealed class JtiReplayCache
{
    public const int DefaultCapacity = 100_000;

    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private long _lastSweepUnix;

    public JtiReplayCache(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
        _capacity = capacity;
    }

    public int Count => _seen.Count;

    /// <summary>
    /// Records <paramref name="jti"/> until <paramref name="forgetAfterUnix"/>. Returns false when it was
    /// already seen (replay) or the cache is full of live entries (fail closed).
    /// </summary>
    public bool TryRegister(string jti, long forgetAfterUnix, long nowUnix)
    {
        Guard.NotNullOrEmpty(jti, nameof(jti));

        if (_seen.Count >= _capacity || nowUnix - Interlocked.Read(ref _lastSweepUnix) >= 30)
            Sweep(nowUnix);

        if (_seen.Count >= _capacity)
            return false;

        if (_seen.TryAdd(jti, forgetAfterUnix))
            return true;

        // Same jti whose window already elapsed cannot pass time checks anyway; still treat as replay.
        return false;
    }

    private void Sweep(long nowUnix)
    {
        Interlocked.Exchange(ref _lastSweepUnix, nowUnix);
        foreach (var entry in _seen)
        {
            if (entry.Value < nowUnix)
                ((ICollection<KeyValuePair<string, long>>)_seen).Remove(entry);
        }
    }
}
