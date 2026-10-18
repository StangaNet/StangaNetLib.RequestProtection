namespace StangaNetLib.RequestProtection.RateLimit.Models;

/// <summary>Defines a single named rate limit rule.</summary>
public sealed record RateLimitPolicy
{
    /// <summary>Policy name — referenced by <c>[RateLimit("Name")]</c> and <c>GlobalPolicyName</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Maximum number of requests allowed within <see cref="WindowSeconds"/>.</summary>
    public int Limit { get; init; } = 60;

    /// <summary>Duration of the fixed window in seconds.</summary>
    public int WindowSeconds { get; init; } = 60;

    /// <summary>Scope used to derive the counter key for each request.</summary>
    public RateLimitScope Scope { get; init; } = RateLimitScope.IP;

    /// <summary>
    /// Header name from which the API key is extracted when <see cref="Scope"/> is
    /// <see cref="RateLimitScope.ApiKey"/>. Default: <c>X-API-Key</c>.
    /// </summary>
    public string ApiKeyHeader { get; init; } = "X-API-Key";
}
