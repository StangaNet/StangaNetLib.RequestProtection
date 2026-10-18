using Microsoft.AspNetCore.Http;
using StangaNetLib.RequestProtection.RateLimit.Models;

namespace StangaNetLib.RequestProtection.RateLimit.Abstractions;

/// <summary>Evaluates a named rate limit policy against the current HTTP request.</summary>
public interface IRateLimitService
{
    /// <summary>
    /// Checks the named policy against <paramref name="context"/> and returns the result.
    /// Returns <see cref="RateLimitResult.PolicyNotFound"/> when the policy is not configured.
    /// </summary>
    /// <param name="policyName">Name of the policy defined in <c>RateLimitingSettings.Policies</c>.</param>
    /// <param name="context">The current HTTP context used to derive the scope key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="RateLimitResult"/> indicating whether the request is allowed and carrying
    /// the standard rate limit header values.
    /// </returns>
    Task<RateLimitResult> CheckAsync(string policyName, HttpContext context, CancellationToken ct = default);
}
