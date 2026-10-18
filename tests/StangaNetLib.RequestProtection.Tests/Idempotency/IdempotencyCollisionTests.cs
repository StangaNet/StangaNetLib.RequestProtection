using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StangaNetLib.Caching.Storage;
using StangaNetLib.RequestProtection.Tests;
using StangaNetLib.RequestProtection.Idempotency.Middleware;
using StangaNetLib.RequestProtection.Idempotency.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace StangaNetLib.RequestProtection.Tests.Idempotency;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class Factory
{
    public static IdempotencyMiddleware Create(RequestDelegate next, ICacheService cache,
        Action<IdempotencyOptions>? configure = null)
    {
        var opts = new IdempotencyOptions();
        configure?.Invoke(opts);
        return new IdempotencyMiddleware(next, cache,
            MsOptions.Create(opts), NullLogger<IdempotencyMiddleware>.Instance);
    }

    public static DefaultHttpContext MakeUserCtx(string userId, string idempotencyKey)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Headers["Idempotency-Key"] = idempotencyKey;
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)]));
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public sealed class IdempotencyCollisionTests
{
    // ── Cross-user isolation ──────────────────────────────────────────────────
    // The cache key is scoped to the authenticated user (sub claim), so two
    // different users sharing the same idempotency key value get completely
    // independent cache entries.

    [Fact]
    public async Task DifferentUsers_SameKey_EachRequestReachesHandler()
    {
        var cache = new FakeCacheService();
        var userACount = 0;
        var userBCount = 0;

        var sut = Factory.Create(async ctx =>
        {
            var uid = ctx.User.FindFirst("sub")!.Value;
            if (uid == "user-A") userACount++;
            else                 userBCount++;
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"response-from-{uid}");
        }, cache);

        await sut.InvokeAsync(Factory.MakeUserCtx("user-A", "shared-key-001"));
        await sut.InvokeAsync(Factory.MakeUserCtx("user-B", "shared-key-001"));

        userACount.Should().Be(1, "user-A's request must reach the handler once");
        userBCount.Should().Be(1, "user-B has an independent scope and must also reach the handler");
    }

    [Fact]
    public async Task DifferentUsers_SameKey_ReplayForOneUserDoesNotAffectOther()
    {
        var cache     = new FakeCacheService();
        var callCount = 0;

        var sut = Factory.Create(async ctx =>
        {
            callCount++;
            ctx.Response.StatusCode  = 200;
            ctx.Response.ContentType = "application/json";
            var uid = ctx.User.FindFirst("sub")!.Value;
            await ctx.Response.WriteAsync($"{{\"user\":\"{uid}\"}}");
        }, cache);

        // First call by user-A — handler invoked and response cached
        await sut.InvokeAsync(Factory.MakeUserCtx("user-A", "order-key-X"));
        // Replay by user-A — must use cache (no handler call)
        var replayA = Factory.MakeUserCtx("user-A", "order-key-X");
        await sut.InvokeAsync(replayA);
        // First call by user-B with same key — independent scope, handler must be invoked
        await sut.InvokeAsync(Factory.MakeUserCtx("user-B", "order-key-X"));

        callCount.Should().Be(2,
            "user-A's replay uses the cache; user-A and user-B each call the handler exactly once");
        replayA.Response.Headers["X-Idempotency-Replayed"].ToString()
            .Should().Be("true", "user-A's second request must be replayed from cache");
    }

    // ── Lock scope isolation ──────────────────────────────────────────────────
    // The in-flight lock key is derived from the same user-scoped cache key.
    // A lock held for user-A must not block user-B even when both use the same
    // idempotency key string.

    [Fact]
    public async Task UserA_InFlightLock_DoesNotBlock_UserB()
    {
        var cache = new FakeCacheService();

        // Manually inject the in-flight lock that would be set for user-A's request.
        // user-A's cache key = "idempotency:user-A:shared-key"
        // user-A's lock key  = "idempotency:user-A:shared-key:lock"
        await cache.SetAsync("idempotency:user-A:shared-key:lock", "1", TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var nextCalledForB = false;
        var sut = Factory.Create(ctx =>
        {
            nextCalledForB = true;
            return Task.CompletedTask;
        }, cache);

        // user-B with the same idempotency key value
        var ctxB = Factory.MakeUserCtx("user-B", "shared-key");
        await sut.InvokeAsync(ctxB);

        ctxB.Response.StatusCode.Should().NotBe(409,
            "user-B's lock key is 'idempotency:user-B:shared-key:lock', not user-A's — no collision");
        nextCalledForB.Should().BeTrue("user-B must reach the handler");
    }

    [Fact]
    public async Task SameUser_InFlightLock_Returns409ForDuplicate()
    {
        var cache = new FakeCacheService();

        // Simulate an in-flight lock for user-A's own request
        await cache.SetAsync("idempotency:user-A:tx-dup:lock", "1", TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var nextCalled = false;
        var sut = Factory.Create(ctx => { nextCalled = true; return Task.CompletedTask; }, cache);

        var ctx = Factory.MakeUserCtx("user-A", "tx-dup");
        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(409,
            "a concurrent duplicate from the same user must be rejected with 409");
        nextCalled.Should().BeFalse("the handler must not be invoked when the lock is held");
    }

    // ── Key sanitization ──────────────────────────────────────────────────────

    [Fact]
    public async Task SafeKey_IsReplayedCorrectly()
    {
        // A key with only word chars and hyphens passes through unchanged;
        // subsequent requests with the same key must replay from cache.
        var cache = new FakeCacheService();
        var calls = 0;

        var sut = Factory.Create(async ctx =>
        {
            calls++;
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("safe-response");
        }, cache);

        await sut.InvokeAsync(Factory.MakeUserCtx("user-Z", "safe-key-0123-abc"));

        var replay = Factory.MakeUserCtx("user-Z", "safe-key-0123-abc");
        await sut.InvokeAsync(replay);

        calls.Should().Be(1, "handler must be called only once for a clean key");
        replay.Response.Headers["X-Idempotency-Replayed"].ToString().Should().Be("true");
    }

    [Fact]
    public async Task KeyWithSpecialChars_MiddlewareDoesNotCrash()
    {
        // Unsafe chars are sanitised to underscores; the middleware must still
        // function correctly without throwing.
        var cache = new FakeCacheService();
        var calls = 0;

        var sut = Factory.Create(async ctx =>
        {
            calls++;
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("sanitized-ok");
        }, cache);

        var ctx = Factory.MakeUserCtx("user-W", "key!@#$%^&*()");
        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        calls.Should().Be(1, "the first request with any key must always reach the handler");
    }
}
