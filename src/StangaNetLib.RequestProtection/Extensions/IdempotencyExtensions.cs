using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StangaNetLib.RequestProtection.Idempotency.Middleware;
using StangaNetLib.RequestProtection.Idempotency.Options;

namespace StangaNetLib.RequestProtection.Extensions;

/// <summary>Extension methods for registering and enabling idempotency deduplication.</summary>
public static class IdempotencyExtensions
{
    /// <summary>
    /// Registers <see cref="IdempotencyMiddleware"/> and binds its options from
    /// the <c>"IdempotencyOptions"</c> configuration section. An optional delegate
    /// applied via <c>PostConfigure</c> can override individual values after binding.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Application configuration used to bind <c>IdempotencyOptions</c>.</param>
    /// <param name="configure">Optional delegate to override specific options after config binding.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// <c>StangaNetLib.Caching</c> must be registered before this call
    /// (<c>services.AddStangaNetLibCaching(...)</c>).
    /// </remarks>
    public static IServiceCollection AddStangaNetLibIdempotency(
        this IServiceCollection services,
        IConfiguration          configuration,
        Action<IdempotencyOptions>? configure = null)
    {
        services.Configure<IdempotencyOptions>(
            configuration.GetSection(IdempotencyOptions.SectionName));

        if (configure is not null)
            services.PostConfigure<IdempotencyOptions>(configure);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IdempotencyMiddleware"/> and configures its options via code only.
    /// Prefer the <c>IConfiguration</c> overload when settings are managed in <c>appsettings.json</c>.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="IdempotencyOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// <c>StangaNetLib.Caching</c> must be registered before this call
    /// (<c>services.AddStangaNetLibCaching(...)</c>).
    /// </remarks>
    public static IServiceCollection AddStangaNetLibIdempotency(
        this IServiceCollection     services,
        Action<IdempotencyOptions>? configure = null)
    {
        services.Configure<IdempotencyOptions>(options => configure?.Invoke(options));
        return services;
    }

    /// <summary>
    /// Adds <see cref="IdempotencyMiddleware"/> to the pipeline.
    /// Place this immediately after <c>UseStangaNetLibAuth()</c>
    /// so that authentication state is available for per-user key scoping.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UseStangaNetLibIdempotency(this IApplicationBuilder app)
    {
        app.UseMiddleware<IdempotencyMiddleware>();
        return app;
    }
}
