namespace StangaNetLib.RequestProtection.RateLimit.Models;

/// <summary>Determines which key is used to bucket requests for a rate limit policy.</summary>
public enum RateLimitScope
{
    /// <summary>One counter per client IP address. Good for login/registration endpoints.</summary>
    IP = 0,

    /// <summary>
    /// One counter per authenticated user ID (<c>sub</c> claim).
    /// Falls back to IP for unauthenticated requests.
    /// </summary>
    User = 1,

    /// <summary>One shared counter for all clients. Use for global throughput caps.</summary>
    Global = 2,

    /// <summary>
    /// One counter per API key extracted from the header named in <c>RateLimitPolicy.ApiKeyHeader</c>
    /// (default: <c>X-API-Key</c>). Falls back to IP when the header is absent.
    /// </summary>
    ApiKey = 3,
}
