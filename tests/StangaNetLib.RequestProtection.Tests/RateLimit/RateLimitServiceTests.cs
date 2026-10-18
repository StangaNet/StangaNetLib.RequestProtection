using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StangaNetLib.RequestProtection.RateLimit.Abstractions;
using StangaNetLib.RequestProtection.RateLimit.Models;
using StangaNetLib.RequestProtection.RateLimit.Services;
using StangaNetLib.RequestProtection.RateLimit.Settings;
using System.Collections.Concurrent;

namespace StangaNetLib.RequestProtection.Tests.RateLimit;

file sealed class FakeCounterStore : IRateLimitCounterStore
{
    private readonly ConcurrentDictionary<string, int> _counters = new();

    public Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var next = _counters.AddOrUpdate(key, 1, (_, c) => c + 1);
        return Task.FromResult(next);
    }
}

public sealed class RateLimitServiceTests
{
    private static RateLimitService CreateSut(int limit = 3, RateLimitScope scope = RateLimitScope.IP)
    {
        var settings = Options.Create(new RateLimitingSettings
        {
            Policies =
            [
                new RateLimitPolicy
                {
                    Name          = "Test",
                    Limit         = limit,
                    WindowSeconds = 60,
                    Scope         = scope,
                },
            ],
        });
        return new RateLimitService(settings, new FakeCounterStore());
    }

    [Fact]
    public async Task CheckAsync_UnknownPolicy_ReturnsAllowed()
    {
        var ct     = TestContext.Current.CancellationToken;
        var sut    = CreateSut();
        var result = await sut.CheckAsync("Unknown", new DefaultHttpContext(), ct);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WithinLimit_ReturnsAllowed()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 5);
        for (var i = 0; i < 5; i++)
        {
            var result = await sut.CheckAsync("Test", new DefaultHttpContext(), ct);
            result.IsAllowed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task CheckAsync_ExceedsLimit_ReturnsRejected()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 2);

        await sut.CheckAsync("Test", new DefaultHttpContext(), ct);
        await sut.CheckAsync("Test", new DefaultHttpContext(), ct);
        var result = await sut.CheckAsync("Test", new DefaultHttpContext(), ct);

        result.IsAllowed.Should().BeFalse();
        result.RetryAfterSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CheckAsync_AllowedResult_PopulatesLimitAndRemaining()
    {
        var ct     = TestContext.Current.CancellationToken;
        var sut    = CreateSut(limit: 10);
        var result = await sut.CheckAsync("Test", new DefaultHttpContext(), ct);

        result.Limit.Should().Be(10);
        result.CurrentCount.Should().Be(1);
        result.Remaining.Should().Be(9);
    }

    [Fact]
    public async Task CheckAsync_RejectedResult_RemainingIsZero()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 1);

        await sut.CheckAsync("Test", new DefaultHttpContext(), ct);
        var result = await sut.CheckAsync("Test", new DefaultHttpContext(), ct);

        result.IsAllowed.Should().BeFalse();
        result.Remaining.Should().Be(0);
    }
}
