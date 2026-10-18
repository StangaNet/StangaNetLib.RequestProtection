namespace StangaNetLib.RequestProtection.Idempotency.Models;

/// <summary>Snapshot of an HTTP response stored in cache for idempotency replay.</summary>
public sealed record IdempotencyResponse
{
    /// <summary>HTTP status code of the original response.</summary>
    public int StatusCode { get; init; }

    /// <summary>Value of the <c>Content-Type</c> header from the original response.</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Response body captured as a UTF-8 string. Suitable for JSON and text responses.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>UTC timestamp at which this snapshot was stored in cache.</summary>
    public DateTimeOffset CachedAt { get; init; }
}
