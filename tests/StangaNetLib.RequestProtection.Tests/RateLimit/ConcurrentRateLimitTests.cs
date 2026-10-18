using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StangaNetLib.RequestProtection.RateLimit.Abstractions;
using StangaNetLib.RequestProtection.RateLimit.Models;
using StangaNetLib.RequestProtection.RateLimit.Services;
using StangaNetLib.RequestProtection.RateLimit.Settings;
using System.Collections.Concurrent;

namespace StangaNetLib.RequestProtection.Tests.RateLimit;

file sealed class AtomicCounterStore : IRateLimitCounterStore
{
    private readonly ConcurrentDictionary<string, int> _counters = new();

    public Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var next = _counters.AddOrUpdate(key, 1, (_, c) => c + 1);
        return Task.FromResult(next);
    }
}

public sealed class ConcurrentRateLimitTests
{
    private static RateLimitService CreateSut(
        int limit,
        RateLimitScope scope = RateLimitScope.IP,
        string policyName = "Test",
        string apiKeyHeader = "X-API-Key")
    {
        var settings = Options.Create(new RateLimitingSettings
        {
            Policies =
            [
                new RateLimitPolicy
                {
                    Name          = policyName,
                    Limit         = limit,
                    WindowSeconds = 60,
                    Scope         = scope,
                    ApiKeyHeader  = apiKeyHeader,
                },
            ],
        });
        return new RateLimitService(settings, new AtomicCounterStore());
    }

    private static DefaultHttpContext MakeIpContext(string ip = "1.2.3.4")
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return ctx;
    }

    private static DefaultHttpContext MakeUserContext(string userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", userId)]));
        return ctx;
    }

    private static DefaultHttpContext MakeApiKeyContext(string apiKey, string headerName = "X-API-Key")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[headerName] = apiKey;
        return ctx;
    }

    // ── Concurrent-style bulk invocations ─────────────────────────────────────
    // The atomic counter store makes the count deterministic regardless of
    // execution order; the tests verify that the service honours exactly `limit`
    // and rejects the rest.

    [Fact]
    public async Task ConcurrentRequests_ExactlyLimitAllowed_RestRejected()
    {
        const int Limit = 10;
        const int Total = 20;
        var sut = CreateSut(Limit);

        var tasks  = Enumerable.Range(0, Total)
            .Select(_ => sut.CheckAsync("Test", MakeIpContext(), TestContext.Current.CancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r.IsAllowed).Should().Be(Limit,
            "exactly the limit number of requests must be allowed");
        results.Count(r => !r.IsAllowed).Should().Be(Total - Limit,
            "all requests beyond the limit must be rejected");
    }

    [Fact]
    public async Task ConcurrentRequests_AllWithinLimit_AllAllowed()
    {
        const int Limit = 20;
        const int Total = 10;
        var sut = CreateSut(Limit);

        var tasks  = Enumerable.Range(0, Total)
            .Select(_ => sut.CheckAsync("Test", MakeIpContext(), TestContext.Current.CancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsAllowed.Should().BeTrue());
    }

    // ── ApiKey scope isolation ────────────────────────────────────────────────

    [Fact]
    public async Task ApiKeyScope_DifferentKeys_CountIndependently()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 1, scope: RateLimitScope.ApiKey);

        var resultAlpha = await sut.CheckAsync("Test", MakeApiKeyContext("key-alpha"), ct);
        var resultBeta  = await sut.CheckAsync("Test", MakeApiKeyContext("key-beta"),  ct);

        resultAlpha.IsAllowed.Should().BeTrue("key-alpha has its own counter starting at 1");
        resultBeta.IsAllowed.Should().BeTrue("key-beta has its own counter starting at 1");
    }

    [Fact]
    public async Task ApiKeyScope_ExhaustingOneKey_DoesNotAffectOther()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 2, scope: RateLimitScope.ApiKey);

        // Exhaust key-A (limit = 2)
        await sut.CheckAsync("Test", MakeApiKeyContext("key-A"), ct);
        await sut.CheckAsync("Test", MakeApiKeyContext("key-A"), ct);
        var exhausted = await sut.CheckAsync("Test", MakeApiKeyContext("key-A"), ct);

        exhausted.IsAllowed.Should().BeFalse("key-A is now rate-limited");

        var resultB = await sut.CheckAsync("Test", MakeApiKeyContext("key-B"), ct);
        resultB.IsAllowed.Should().BeTrue("key-B has an independent counter and is not exhausted");
    }

    [Fact]
    public async Task ApiKeyScope_MissingHeader_FallsBackToIp()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 5, scope: RateLimitScope.ApiKey);

        // No API key header — service falls back to IP
        var ctx    = MakeIpContext("9.9.9.9");
        var result = await sut.CheckAsync("Test", ctx, ct);

        result.IsAllowed.Should().BeTrue("IP fallback counter starts fresh");
    }

    // ── User scope isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task UserScope_DifferentUsers_CountIndependently()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 1, scope: RateLimitScope.User);

        var resultUser1 = await sut.CheckAsync("Test", MakeUserContext("user-001"), ct);
        var resultUser2 = await sut.CheckAsync("Test", MakeUserContext("user-002"), ct);

        resultUser1.IsAllowed.Should().BeTrue();
        resultUser2.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task UserScope_ExhaustingOneUser_DoesNotAffectOther()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 1, scope: RateLimitScope.User);

        await sut.CheckAsync("Test", MakeUserContext("user-A"), ct);
        var exhausted = await sut.CheckAsync("Test", MakeUserContext("user-A"), ct);

        exhausted.IsAllowed.Should().BeFalse("user-A is rate-limited");

        var resultB = await sut.CheckAsync("Test", MakeUserContext("user-B"), ct);
        resultB.IsAllowed.Should().BeTrue("user-B has an independent counter");
    }

    // ── Global scope ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GlobalScope_DifferentIps_ShareSingleCounter()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 2, scope: RateLimitScope.Global);

        // Three requests from three different IPs — all share one global counter
        await sut.CheckAsync("Test", MakeIpContext("1.1.1.1"), ct);
        await sut.CheckAsync("Test", MakeIpContext("2.2.2.2"), ct);
        var third = await sut.CheckAsync("Test", MakeIpContext("3.3.3.3"), ct);

        third.IsAllowed.Should().BeFalse("global scope exhausted after 2 requests from any IPs");
    }

    [Fact]
    public async Task GlobalScope_WithinLimit_AllIpsAllowed()
    {
        var ct  = TestContext.Current.CancellationToken;
        var sut = CreateSut(limit: 5, scope: RateLimitScope.Global);

        for (var i = 0; i < 5; i++)
        {
            var result = await sut.CheckAsync("Test", MakeIpContext($"10.0.0.{i + 1}"), ct);
            result.IsAllowed.Should().BeTrue($"request {i + 1} of 5 from a unique IP must be allowed");
        }
    }
}
