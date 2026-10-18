using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StangaNetLib.RequestProtection.RateLimit.Abstractions;
using StangaNetLib.RequestProtection.RateLimit.Models;
using StangaNetLib.RequestProtection.RateLimit.Settings;

namespace StangaNetLib.RequestProtection.RateLimit.Services;

/// <summary>
/// Default <see cref="IRateLimitService"/> implementation.
/// Evaluates named policies from <see cref="RateLimitingSettings"/> using the configured
/// <see cref="IRateLimitCounterStore"/>.
/// </summary>
public sealed class RateLimitService : IRateLimitService
{
    private readonly IOptions<RateLimitingSettings> _settings;
    private readonly IRateLimitCounterStore         _store;

    /// <param name="settings">Rate limiting configuration containing named policies.</param>
    /// <param name="store">Counter store used to track request counts per window bucket.</param>
    public RateLimitService(IOptions<RateLimitingSettings> settings, IRateLimitCounterStore store)
    {
        _settings = settings;
        _store    = store;
    }

    /// <summary>Checks the named policy against the current request and returns the rate limit result.</summary>
    /// <param name="policyName">Name of the policy to evaluate.</param>
    /// <param name="context">The current HTTP context used to derive the scope key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="RateLimitResult"/> with <see cref="RateLimitResult.IsAllowed"/> set to <c>false</c>
    /// when the policy limit is exceeded, or <see cref="RateLimitResult.PolicyNotFound"/> when the
    /// policy name is unknown or the policy is misconfigured.
    /// </returns>
    public async Task<RateLimitResult> CheckAsync(
        string policyName, HttpContext context, CancellationToken ct = default)
    {
        var policy = _settings.Value.Policies
            .FirstOrDefault(p => p.Name.Equals(policyName, StringComparison.OrdinalIgnoreCase));

        if (policy is null)
            return RateLimitResult.PolicyNotFound(policyName);

        // Guard: misconfigured policy — treat as not found to avoid a DivideByZeroException.
        if (policy.WindowSeconds <= 0 || policy.Limit <= 0)
            return RateLimitResult.PolicyNotFound(policyName);

        var now          = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowBucket = now / policy.WindowSeconds;
        var resetAt      = (windowBucket + 1) * policy.WindowSeconds;
        var scopeKey     = GetScopeKey(policy, context);
        var cacheKey     = $"rl:{policy.Name}:{scopeKey}:{windowBucket}";

        var count = await _store.IncrementAsync(cacheKey, TimeSpan.FromSeconds(policy.WindowSeconds), ct);

        return count <= policy.Limit
            ? RateLimitResult.Allow(policy.Name, policy.Limit, count, resetAt)
            : RateLimitResult.Reject(policy.Name, policy.Limit, resetAt);
    }

    private static string GetScopeKey(RateLimitPolicy policy, HttpContext context)
    {
        return policy.Scope switch
        {
            RateLimitScope.IP => context.Connection.RemoteIpAddress?.ToString() ?? "unknown",

            RateLimitScope.User =>
                context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",

            RateLimitScope.Global => "global",

            RateLimitScope.ApiKey =>
                context.Request.Headers[policy.ApiKeyHeader].FirstOrDefault()
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",

            _ => "unknown",
        };
    }
}
