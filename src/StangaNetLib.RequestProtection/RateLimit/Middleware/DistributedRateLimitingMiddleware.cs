using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StangaNetLib.RequestProtection.RateLimit.Attributes;
using StangaNetLib.RequestProtection.RateLimit.Models;
using StangaNetLib.RequestProtection.RateLimit.Abstractions;
using StangaNetLib.RequestProtection.RateLimit.Settings;

namespace StangaNetLib.RequestProtection.RateLimit.Middleware;

/// <summary>
/// ASP.NET Core middleware that enforces rate limiting using named policies.
/// Evaluates <c>[RateLimit]</c> attributes on the matched endpoint; falls back to
/// <see cref="RateLimitingSettings.GlobalPolicyName"/> when no attribute is present.
/// Returns HTTP 429 with <c>Retry-After</c> and <c>X-RateLimit-*</c> headers on violation.
/// </summary>
public sealed class DistributedRateLimitingMiddleware
{
    private readonly RequestDelegate                            _next;
    private readonly IRateLimitService                          _rateLimitService;
    private readonly IOptions<RateLimitingSettings>             _settings;
    private readonly ILogger<DistributedRateLimitingMiddleware> _logger;

    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="rateLimitService">Service used to evaluate rate limit policies.</param>
    /// <param name="settings">Rate limiting configuration.</param>
    /// <param name="logger">Logger for rate limit violation warnings.</param>
    public DistributedRateLimitingMiddleware(
        RequestDelegate                             next,
        IRateLimitService                           rateLimitService,
        IOptions<RateLimitingSettings>              settings,
        ILogger<DistributedRateLimitingMiddleware>  logger)
    {
        _next             = next;
        _rateLimitService = rateLimitService;
        _settings         = settings;
        _logger           = logger;
    }

    /// <summary>
    /// Evaluates all applicable rate limit policies for the request.
    /// Calls the next middleware when all policies allow the request; otherwise short-circuits with HTTP 429.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var policies = GetApplicablePolicies(context);

        foreach (var policyName in policies)
        {
            var result = await _rateLimitService.CheckAsync(policyName, context, context.RequestAborted);

            WriteRateLimitHeaders(context, result);

            if (!result.IsAllowed)
            {
                _logger.LogWarning(
                    "Rate limit exceeded — policy={Policy} scope={Scope} retryAfter={RetryAfter}s",
                    result.PolicyName,
                    context.Connection.RemoteIpAddress,
                    result.RetryAfterSeconds);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
                await context.Response.WriteAsJsonAsync(new
                {
                    error             = "Too many requests. Please slow down.",
                    retryAfterSeconds = result.RetryAfterSeconds,
                    policy            = result.PolicyName,
                });
                return;
            }
        }

        await _next(context);
    }

    private IReadOnlyList<string> GetApplicablePolicies(HttpContext context)
    {
        var endpoint   = context.GetEndpoint();
        var attributes = endpoint?.Metadata.GetOrderedMetadata<RateLimitAttribute>() ?? [];
        var policies   = attributes.Select(a => a.PolicyName).ToList();

        if (policies.Count == 0 && _settings.Value.GlobalPolicyName is { } global)
            policies.Add(global);

        return policies;
    }

    private static void WriteRateLimitHeaders(HttpContext context, RateLimitResult result)
    {
        if (result.Limit <= 0) return;

        context.Response.Headers["X-RateLimit-Limit"]     = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"]     = result.ResetAtEpoch.ToString();
    }
}
