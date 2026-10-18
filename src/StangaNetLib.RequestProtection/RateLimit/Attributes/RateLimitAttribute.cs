namespace StangaNetLib.RequestProtection.RateLimit.Attributes;

/// <summary>
/// Applies the named rate limit policy to the decorated controller or action.
/// Stackable: multiple attributes are evaluated independently — any exceeded policy rejects the request.
/// </summary>
/// <example>
/// <code>
/// [RateLimit("Auth")]
/// [HttpPost("login")]
/// public IActionResult Login(...) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RateLimitAttribute : Attribute
{
    /// <summary>The name of the policy defined in <c>RateLimitingSettings.Policies</c>.</summary>
    public string PolicyName { get; }

    /// <param name="policyName">Name of the policy defined in <c>RateLimitingSettings.Policies</c>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="policyName"/> is null or whitespace.</exception>
    public RateLimitAttribute(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
            throw new ArgumentException("Policy name must not be empty.", nameof(policyName));

        PolicyName = policyName;
    }
}
