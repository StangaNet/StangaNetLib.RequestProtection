namespace StangaNetLib.RequestProtection.Idempotency.Options;

/// <summary>
/// Configuration for <c>IdempotencyMiddleware</c>. Bind from <c>appsettings.json</c> under
/// <c>"IdempotencyOptions"</c> or configure via <c>AddStangaNetLibIdempotency(configuration)</c>.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>Configuration section name used to bind this settings class. Value: <c>"IdempotencyOptions"</c>.</summary>
    public const string SectionName = "IdempotencyOptions";

    /// <summary>Name of the request header carrying the client-generated idempotency key. Default: "Idempotency-Key".</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>How long a successful response is stored in cache before being evicted. Default: 60 minutes.</summary>
    public int KeyExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// HTTP methods (case-insensitive) whose requests are subject to idempotency processing.
    /// Default: POST and PATCH. PUT is naturally idempotent by REST convention and is excluded by default.
    /// Configurable from <c>appsettings.json</c> as a string array, e.g. <c>["POST","PATCH"]</c>.
    /// </summary>
    public ICollection<string> ProtectedMethods { get; set; } = ["POST", "PATCH"];

    /// <summary>
    /// Duration of the in-flight processing lock used to detect and reject concurrent duplicate requests.
    /// Must be shorter than <see cref="KeyExpiryMinutes"/>. Default: 30 seconds.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal bool IsProtectedMethod(string httpMethod)
        => ProtectedMethods.Any(m => m.Equals(httpMethod, StringComparison.OrdinalIgnoreCase));
}
