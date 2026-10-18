using StangaNetLib.RequestProtection.RateLimit.Models;

namespace StangaNetLib.RequestProtection.RateLimit.Settings;

/// <summary>
/// Configuration root for rate limiting. Bind from <c>appsettings.json</c> under
/// <c>"RateLimitingSettings"</c> or via <c>AddStangaNetLibRateLimit(configuration)</c>.
/// </summary>
public sealed class RateLimitingSettings
{
    /// <summary>Configuration section name used to bind this settings class. Value: <c>"RateLimitingSettings"</c>.</summary>
    public const string SectionName = "RateLimitingSettings";

    /// <summary>Named rate limit policies. Referenced by <c>[RateLimit("PolicyName")]</c> attributes.</summary>
    public List<RateLimitPolicy> Policies { get; init; } = [];

    /// <summary>
    /// If set, applies this policy to every request that has no <c>[RateLimit]</c> attribute.
    /// Must match one of the names in <see cref="Policies"/>.
    /// </summary>
    public string? GlobalPolicyName { get; init; }
}
