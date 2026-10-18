namespace StangaNetLib.RequestProtection.RateLimit.Models;

/// <summary>Result of a rate limit check, carrying all data needed for response headers and 429 body.</summary>
public sealed record RateLimitResult
{
    /// <summary><c>true</c> if the request is within the policy limit; <c>false</c> if it should be rejected.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>Name of the policy that produced this result.</summary>
    public string PolicyName { get; init; } = string.Empty;

    /// <summary>Configured maximum requests per window.</summary>
    public int Limit { get; init; }

    /// <summary>Counter value after the current increment.</summary>
    public int CurrentCount { get; init; }

    /// <summary>Remaining requests in the current window. Zero when the limit is exceeded.</summary>
    public int Remaining => Math.Max(0, Limit - CurrentCount);

    /// <summary>Unix timestamp (seconds) at which the current window resets.</summary>
    public long ResetAtEpoch { get; init; }

    /// <summary>Seconds until the window resets. Populated only when <see cref="IsAllowed"/> is <c>false</c>.</summary>
    public int RetryAfterSeconds { get; init; }

    /// <summary>Creates an allowed result with the current counter state.</summary>
    /// <param name="policyName">Policy that was evaluated.</param>
    /// <param name="limit">Configured request limit for the window.</param>
    /// <param name="currentCount">Counter value after increment.</param>
    /// <param name="resetAt">Unix timestamp when the window resets.</param>
    /// <returns>A <see cref="RateLimitResult"/> with <see cref="IsAllowed"/> set to <c>true</c>.</returns>
    public static RateLimitResult Allow(string policyName, int limit, int currentCount, long resetAt)
        => new()
        {
            IsAllowed    = true,
            PolicyName   = policyName,
            Limit        = limit,
            CurrentCount = currentCount,
            ResetAtEpoch = resetAt,
        };

    /// <summary>Creates a rejected result for a request that exceeded the policy limit.</summary>
    /// <param name="policyName">Policy that was violated.</param>
    /// <param name="limit">Configured request limit for the window.</param>
    /// <param name="resetAt">Unix timestamp when the window resets.</param>
    /// <returns>A <see cref="RateLimitResult"/> with <see cref="IsAllowed"/> set to <c>false</c> and <see cref="RetryAfterSeconds"/> populated.</returns>
    public static RateLimitResult Reject(string policyName, int limit, long resetAt)
        => new()
        {
            IsAllowed         = false,
            PolicyName        = policyName,
            Limit             = limit,
            CurrentCount      = limit + 1,
            ResetAtEpoch      = resetAt,
            RetryAfterSeconds = (int)Math.Max(0, resetAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };

    /// <summary>
    /// Creates a pass-through result used when the policy name is not found in settings.
    /// Always allows the request.
    /// </summary>
    /// <param name="policyName">The unresolved policy name, carried for diagnostic purposes.</param>
    /// <returns>A <see cref="RateLimitResult"/> with <see cref="IsAllowed"/> set to <c>true</c> and zero limit fields.</returns>
    public static RateLimitResult PolicyNotFound(string policyName)
        => new() { IsAllowed = true, PolicyName = policyName };
}
