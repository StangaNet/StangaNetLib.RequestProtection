using FluentAssertions;
using StangaNetLib.Caching.Storage;
using StangaNetLib.RequestProtection.Tests;
using StangaNetLib.RequestProtection.RateLimit.Stores;

namespace StangaNetLib.RequestProtection.Tests.RateLimit;
public sealed class CacheRateLimitCounterStoreTests
{
    private static CacheRateLimitCounterStore CreateSut() => new(new FakeCacheService());

    [Fact]
    public async Task IncrementAsync_FirstCall_ReturnsOne()
    {
        var ct    = TestContext.Current.CancellationToken;
        var count = await CreateSut().IncrementAsync("key1", TimeSpan.FromSeconds(60), ct);
        count.Should().Be(1);
    }

    [Fact]
    public async Task IncrementAsync_SubsequentCalls_IncrementsSequentially()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut();
        for (var i = 1; i <= 5; i++)
            (await sut.IncrementAsync("key2", TimeSpan.FromSeconds(60), ct)).Should().Be(i);
    }

    [Fact]
    public async Task IncrementAsync_DifferentKeys_CountIndependently()
    {
        var ct     = TestContext.Current.CancellationToken;
        var sut    = CreateSut();
        var countA = await sut.IncrementAsync("keyA", TimeSpan.FromSeconds(60), ct);
        var countB = await sut.IncrementAsync("keyB", TimeSpan.FromSeconds(60), ct);
        countA.Should().Be(1);
        countB.Should().Be(1);
    }
}
