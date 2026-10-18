using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StangaNetLib.Caching.Storage;
using StangaNetLib.RequestProtection.Idempotency.Models;
using StangaNetLib.RequestProtection.Idempotency.Options;

namespace StangaNetLib.RequestProtection.Idempotency.Middleware;

/// <summary>
/// ASP.NET Core middleware that enforces idempotency for configured HTTP methods.
/// On the first request with a given <c>Idempotency-Key</c> the response is captured and cached.
/// Subsequent requests with the same key replay the cached response without invoking the handler.
/// Concurrent duplicate requests (same key, handler still in-flight) are rejected with HTTP 409.
/// </summary>
public sealed partial class IdempotencyMiddleware
{
    [GeneratedRegex(@"[^\w\-]")]
    private static partial Regex UnsafeCharsRegex();

    private readonly RequestDelegate _next;
    private readonly ICacheService _cache;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="cache">Cache service used to store and retrieve idempotency snapshots.</param>
    /// <param name="options">Idempotency configuration.</param>
    /// <param name="logger">Logger for cache hits and concurrent duplicate warnings.</param>
    public IdempotencyMiddleware(
        RequestDelegate next,
        ICacheService cache,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Processes the request through the idempotency filter.
    /// Passes through immediately for non-protected methods or requests without an idempotency key.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.IsProtectedMethod(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers[_options.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await _next(context);
            return;
        }

        var cacheKey = BuildCacheKey(context, idempotencyKey);
        var lockKey = $"{cacheKey}:lock";

        var cachedResult = await _cache.GetAsync<IdempotencyResponse>(cacheKey);
        if (cachedResult is { IsSuccess: true, Value: { } cached })
        {
            _logger.LogDebug("Idempotency cache hit for key {Key}", idempotencyKey);
            context.Response.Headers["X-Idempotency-Replayed"] = "true";
            await WriteCachedResponseAsync(context, cached);
            return;
        }

        var lockExists = await _cache.ExistsAsync(lockKey);
        if (lockExists is { IsSuccess: true, Value: true })
        {
            _logger.LogWarning(
                "Concurrent duplicate request detected for idempotency key {Key}", idempotencyKey);

            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "A request with this Idempotency-Key is already being processed. " +
                        "Retry after the original request completes.",
            });
            return;
        }

        // Use a string sentinel — ICacheService requires T : class (bool is a value type).
        await _cache.SetAsync(lockKey, "1", _options.LockTimeout);

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            buffer.Seek(0, SeekOrigin.Begin);
            var body = await new StreamReader(buffer).ReadToEndAsync();

            // Only cache successful responses; non-2xx results leave the key uncached
            // so the client can retry with the same idempotency key after fixing the issue.
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                var response = new IdempotencyResponse
                {
                    StatusCode = context.Response.StatusCode,
                    ContentType = context.Response.ContentType ?? string.Empty,
                    Body = body,
                    CachedAt = DateTimeOffset.UtcNow,
                };

                await _cache.SetAsync(
                    cacheKey, response, TimeSpan.FromMinutes(_options.KeyExpiryMinutes));

                _logger.LogDebug(
                    "Idempotency response cached for key {Key} (HTTP {Status})",
                    idempotencyKey, context.Response.StatusCode);
            }
        }
        finally
        {
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            await _cache.RemoveAsync(lockKey);
        }
    }

    private string BuildCacheKey(HttpContext context, string idempotencyKey)
    {
        // Scope the key to the authenticated user (or IP) to prevent cross-user response replay.
        var scope = context.User.FindFirst("sub")?.Value
                 ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? context.Connection.RemoteIpAddress?.ToString()
                 ?? "anonymous";

        var safeKey = UnsafeCharsRegex().Replace(idempotencyKey, "_");
        return $"idempotency:{scope}:{safeKey}";
    }

    private static async Task WriteCachedResponseAsync(HttpContext context, IdempotencyResponse cached)
    {
        context.Response.StatusCode = cached.StatusCode;

        if (!string.IsNullOrEmpty(cached.ContentType))
            context.Response.ContentType = cached.ContentType;

        await context.Response.WriteAsync(cached.Body);
    }
}
