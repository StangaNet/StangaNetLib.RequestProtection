using StangaNetLib.Caching.Storage;
using StangaNetLib.RequestProtection.RateLimit.Abstractions;

namespace StangaNetLib.RequestProtection.RateLimit.Stores;

/// <summary>
/// <see cref="ICacheService"/>-backed counter store.
/// Uses a fixed-window bucket key with a non-atomic Get+Set.
/// For strict atomic counting replace with a Redis INCR-based implementation.
/// </summary>
public sealed class CacheRateLimitCounterStore : IRateLimitCounterStore
{
    private readonly ICacheService _cache;

    /// <param name="cache">Cache service used to persist window counters.</param>
    public CacheRateLimitCounterStore(ICacheService cache)
        => _cache = cache;

    /// <inheritdoc/>
    public async Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var stored = await _cache.GetAsync<string>(key, ct);
        var current = stored is { IsSuccess: true, Value: { } value }
            ? int.Parse(value)
            : 0;
        var next = current + 1;

        // The bucket key encodes the window boundary, so it is abandoned automatically when the
        // next window starts. A TTL of one window is sufficient.
        await _cache.SetAsync(key, next.ToString(), window, ct);

        return next;
    }
}
