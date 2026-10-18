using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using StangaNetLib.Caching.Storage;
using StangaNetLib.Core.Common;

namespace StangaNetLib.RequestProtection.Tests;

/// <summary>In-memory <see cref="ICacheService"/> for unit tests.</summary>
internal sealed class FakeCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, object> _store = new(StringComparer.Ordinal);

    public Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        _store.TryGetValue(key, out var v);
        return Task.FromResult(Result<T?>.Success(v is T t ? t : null));
    }

    public Task<Result> SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        _store[key] = value!;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<bool>.Success(_store.ContainsKey(key)));

    public async Task<Result<T>> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var existing = await GetAsync<T>(key, cancellationToken);
        if (existing is { IsSuccess: true, Value: { } value })
            return Result<T>.Success(value);

        var created = await factory(cancellationToken);
        await SetAsync(key, created, expiration, cancellationToken);
        return Result<T>.Success(created);
    }

    public Task<Result> RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        foreach (var k in _store.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
            _store.TryRemove(k, out _);
        return Task.FromResult(Result.Success());
    }

    public async IAsyncEnumerable<string> ScanKeysAsync(
        string pattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var k in _store.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return k;
        }

        await Task.CompletedTask;
    }

    public Task<Result<IReadOnlyDictionary<string, T?>>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default) where T : class
    {
        var result = keys.ToDictionary(
            k => k,
            k => _store.TryGetValue(k, out var v) && v is T t ? t : default(T?));
        return Task.FromResult(Result<IReadOnlyDictionary<string, T?>>.Success(result));
    }

    public Task<Result> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> entries,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        foreach (var (k, v) in entries)
            _store[k] = v!;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var k in keys)
            _store.TryRemove(k, out _);
        return Task.FromResult(Result.Success());
    }
}
