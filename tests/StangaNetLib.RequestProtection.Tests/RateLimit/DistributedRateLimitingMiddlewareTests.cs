using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StangaNetLib.RequestProtection.RateLimit.Abstractions;
using StangaNetLib.RequestProtection.RateLimit.Middleware;
using StangaNetLib.RequestProtection.RateLimit.Models;
using StangaNetLib.RequestProtection.RateLimit.Services;
using StangaNetLib.RequestProtection.RateLimit.Settings;
using System.Collections.Concurrent;

namespace StangaNetLib.RequestProtection.Tests.RateLimit;

file sealed class FakeCounterStore2 : IRateLimitCounterStore
{
    private readonly ConcurrentDictionary<string, int> _counters = new();

    public Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var next = _counters.AddOrUpdate(key, 1, (_, c) => c + 1);
        return Task.FromResult(next);
    }
}

public sealed class DistributedRateLimitingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoEndpointNoGlobal_CallsNext()
    {
        var called   = false;
        var settings = Options.Create(new RateLimitingSettings { Policies = [] });
        var service  = new RateLimitService(settings, new FakeCounterStore2());
        var sut      = new DistributedRateLimitingMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            service, settings, NullLogger<DistributedRateLimitingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new System.IO.MemoryStream();
        await sut.InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_GlobalPolicy_WithinLimit_CallsNext()
    {
        var called   = false;
        var settings = Options.Create(new RateLimitingSettings
        {
            Policies          = [new RateLimitPolicy { Name = "Global", Limit = 10, WindowSeconds = 60 }],
            GlobalPolicyName  = "Global",
        });
        var service = new RateLimitService(settings, new FakeCounterStore2());
        var sut     = new DistributedRateLimitingMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            service, settings, NullLogger<DistributedRateLimitingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new System.IO.MemoryStream();
        await sut.InvokeAsync(context);

        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_GlobalPolicy_ExceedsLimit_Returns429()
    {
        var settings = Options.Create(new RateLimitingSettings
        {
            Policies         = [new RateLimitPolicy { Name = "Tight", Limit = 1, WindowSeconds = 60 }],
            GlobalPolicyName = "Tight",
        });
        var store   = new FakeCounterStore2();
        var service = new RateLimitService(settings, store);
        var sut     = new DistributedRateLimitingMiddleware(
            _ => Task.CompletedTask,
            service, settings, NullLogger<DistributedRateLimitingMiddleware>.Instance);

        var ctx1 = new DefaultHttpContext();
        ctx1.Response.Body = new System.IO.MemoryStream();
        await sut.InvokeAsync(ctx1);
        ctx1.Response.StatusCode.Should().Be(200);

        var ctx2 = new DefaultHttpContext();
        ctx2.Response.Body = new System.IO.MemoryStream();
        await sut.InvokeAsync(ctx2);
        ctx2.Response.StatusCode.Should().Be(429);
        ctx2.Response.Headers.RetryAfter.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_RateLimitedResponse_IncludesStandardHeaders()
    {
        var settings = Options.Create(new RateLimitingSettings
        {
            Policies         = [new RateLimitPolicy { Name = "H", Limit = 5, WindowSeconds = 60 }],
            GlobalPolicyName = "H",
        });
        var service = new RateLimitService(settings, new FakeCounterStore2());
        var sut     = new DistributedRateLimitingMiddleware(
            _ => Task.CompletedTask,
            service, settings, NullLogger<DistributedRateLimitingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new System.IO.MemoryStream();
        await sut.InvokeAsync(context);

        context.Response.Headers.Keys.Should().Contain("X-RateLimit-Limit");
        context.Response.Headers.Keys.Should().Contain("X-RateLimit-Remaining");
        context.Response.Headers.Keys.Should().Contain("X-RateLimit-Reset");
    }
}
