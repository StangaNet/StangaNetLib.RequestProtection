using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StangaNetLib.Caching.Storage;
using StangaNetLib.RequestProtection.Tests;
using StangaNetLib.RequestProtection.Idempotency.Middleware;
using StangaNetLib.RequestProtection.Idempotency.Models;
using StangaNetLib.RequestProtection.Idempotency.Options;
using System.Text;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace StangaNetLib.RequestProtection.Tests.Idempotency;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class MiddlewareFactory
{
    public static IdempotencyMiddleware Create(
        RequestDelegate             next,
        ICacheService               cache,
        Action<IdempotencyOptions>? configure = null)
    {
        var opts = new IdempotencyOptions();
        configure?.Invoke(opts);
        return new IdempotencyMiddleware(
            next, cache,
            MsOptions.Create(opts),
            NullLogger<IdempotencyMiddleware>.Instance);
    }

    public static DefaultHttpContext MakeContext(
        string  method          = "POST",
        string? idempotencyKey  = null,
        string  headerName      = "Idempotency-Key")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Response.Body  = new MemoryStream();
        if (idempotencyKey is not null)
            ctx.Request.Headers[headerName] = idempotencyKey;
        return ctx;
    }

    public static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public sealed class IdempotencyMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_GetRequest_CallsNext()
    {
        var nextCalled = false;
        var sut = MiddlewareFactory.Create(_ => { nextCalled = true; return Task.CompletedTask; }, new FakeCacheService());
        var ctx = MiddlewareFactory.MakeContext(method: "GET");

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_PostWithoutKey_CallsNext()
    {
        var nextCalled = false;
        var sut = MiddlewareFactory.Create(_ => { nextCalled = true; return Task.CompletedTask; }, new FakeCacheService());
        var ctx = MiddlewareFactory.MakeContext(method: "POST");

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    public async Task InvokeAsync_NonProtectedMethod_CallsNext(string method)
    {
        var nextCalled = false;
        var sut = MiddlewareFactory.Create(_ => { nextCalled = true; return Task.CompletedTask; }, new FakeCacheService());
        var ctx = MiddlewareFactory.MakeContext(method: method, idempotencyKey: "key-1");

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_FirstRequest_ProcessesAndCachesResponse()
    {
        var cache = new FakeCacheService();
        var sut   = MiddlewareFactory.Create(
            async ctx =>
            {
                ctx.Response.StatusCode  = 201;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"id\":42}");
            },
            cache);

        var ctx = MiddlewareFactory.MakeContext(idempotencyKey: "create-user-001");
        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        var body = await MiddlewareFactory.ReadResponseBodyAsync(ctx);
        body.Should().Be("{\"id\":42}");

        var cached = await cache.GetAsync<IdempotencyResponse>(
            "idempotency:anonymous:create-user-001", TestContext.Current.CancellationToken);
        cached.IsSuccess.Should().BeTrue();
        cached.Value.Should().NotBeNull();
        cached.Value!.StatusCode.Should().Be(201);
        cached.Value.Body.Should().Be("{\"id\":42}");
    }

    [Fact]
    public async Task InvokeAsync_ReplayRequest_ReturnsCachedResponseWithoutCallingNext()
    {
        var cache      = new FakeCacheService();
        var nextCalled = false;

        var sut = MiddlewareFactory.Create(
            async ctx =>
            {
                nextCalled = true;
                ctx.Response.StatusCode  = 201;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"id\":42}");
            },
            cache);

        var ctx1 = MiddlewareFactory.MakeContext(idempotencyKey: "order-777");
        await sut.InvokeAsync(ctx1);
        nextCalled.Should().BeTrue();

        nextCalled = false;
        var ctx2 = MiddlewareFactory.MakeContext(idempotencyKey: "order-777");
        await sut.InvokeAsync(ctx2);

        nextCalled.Should().BeFalse("cached response must be returned without reaching the handler");
        ctx2.Response.StatusCode.Should().Be(201);
        ctx2.Response.Headers["X-Idempotency-Replayed"].ToString().Should().Be("true");

        var body = await MiddlewareFactory.ReadResponseBodyAsync(ctx2);
        body.Should().Be("{\"id\":42}");
    }

    [Fact]
    public async Task InvokeAsync_ErrorResponse_NotCached()
    {
        var cache     = new FakeCacheService();
        var callCount = 0;

        var sut = MiddlewareFactory.Create(
            async ctx =>
            {
                callCount++;
                ctx.Response.StatusCode  = 422;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":\"invalid\"}");
            },
            cache);

        await sut.InvokeAsync(MiddlewareFactory.MakeContext(idempotencyKey: "bad-req-1"));
        await sut.InvokeAsync(MiddlewareFactory.MakeContext(idempotencyKey: "bad-req-1"));

        callCount.Should().Be(2, "non-2xx responses must not be cached");
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentDuplicate_Returns409()
    {
        var cache = new FakeCacheService();

        await cache.SetAsync("idempotency:anonymous:tx-999:lock", "1",
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var nextCalled = false;
        var sut = MiddlewareFactory.Create(_ => { nextCalled = true; return Task.CompletedTask; }, cache);
        var ctx = MiddlewareFactory.MakeContext(idempotencyKey: "tx-999");
        ctx.Response.Body = new MemoryStream();

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrows_LockIsReleased()
    {
        var cache = new FakeCacheService();
        var sut   = MiddlewareFactory.Create(
            _ => throw new InvalidOperationException("boom"),
            cache);

        var ctx = MiddlewareFactory.MakeContext(idempotencyKey: "failing-req");
        ctx.Response.Body = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InvokeAsync(ctx));

        var lockExists = await cache.ExistsAsync(
            "idempotency:anonymous:failing-req:lock", TestContext.Current.CancellationToken);
        lockExists.IsSuccess.Should().BeTrue();
        lockExists.Value.Should().BeFalse("lock must be released even when the handler throws");
    }

    [Fact]
    public async Task InvokeAsync_CustomHeaderName_IsRespected()
    {
        var nextCalled = false;
        var sut = MiddlewareFactory.Create(
            async ctx => { nextCalled = true; ctx.Response.StatusCode = 200; await ctx.Response.WriteAsync("ok"); },
            new FakeCacheService(),
            opts => opts.HeaderName = "X-Request-Id");

        var ctxStandard = MiddlewareFactory.MakeContext(
            idempotencyKey: "abc", headerName: "Idempotency-Key");
        await sut.InvokeAsync(ctxStandard);
        nextCalled.Should().BeTrue();

        nextCalled = false;
        var ctxCustom = MiddlewareFactory.MakeContext(
            idempotencyKey: "abc", headerName: "X-Request-Id");
        await sut.InvokeAsync(ctxCustom);
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Replay_SetsXIdempotencyReplayedHeader()
    {
        var cache = new FakeCacheService();
        var sut   = MiddlewareFactory.Create(
            async ctx => { ctx.Response.StatusCode = 200; await ctx.Response.WriteAsync("{}"); },
            cache);

        await sut.InvokeAsync(MiddlewareFactory.MakeContext(idempotencyKey: "key-replay"));

        var ctx = MiddlewareFactory.MakeContext(idempotencyKey: "key-replay");
        await sut.InvokeAsync(ctx);

        ctx.Response.Headers["X-Idempotency-Replayed"].ToString().Should().Be("true");
    }
}
