namespace StangaNetLib.RequestProtection.RateLimit.Abstractions;

/// <summary>
/// Increments a named counter within a fixed window and returns the new count.
/// </summary>
/// <remarks>
/// Implementations should be atomic where possible (Redis INCR + EXPIRE).
/// The default <see cref="Stores.CacheRateLimitCounterStore"/> uses
/// <c>ICacheService</c> Get+Set, which has a small race window under high concurrency.
/// </remarks>
public interface IRateLimitCounterStore
{
    /// <summary>
    /// Increments the counter identified by <paramref name="key"/> and returns the new count.
    /// The counter must expire no earlier than <paramref name="window"/> from the first increment.
    /// </summary>
    /// <param name="key">Unique counter key, typically scoped to a policy, client, and time bucket.</param>
    /// <param name="window">Minimum TTL for the counter entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The counter value after the increment (starts at 1 for a fresh key).</returns>
    Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default);
}
