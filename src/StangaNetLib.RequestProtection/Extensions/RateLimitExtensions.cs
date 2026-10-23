using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StangaNetLib.RequestProtection.RateLimit.Abstractions;
using StangaNetLib.RequestProtection.RateLimit.Middleware;
using StangaNetLib.RequestProtection.RateLimit.Models;
using StangaNetLib.RequestProtection.RateLimit.Services;
using StangaNetLib.RequestProtection.RateLimit.Settings;
using StangaNetLib.RequestProtection.RateLimit.Stores;

namespace StangaNetLib.RequestProtection.Extensions;

/// <summary>Extension methods for registering and enabling distributed rate limiting.</summary>
public static class RateLimitExtensions
{
    /// <summary>
    /// Registers <see cref="IRateLimitService"/> (<see cref="RateLimitService"/>),
    /// <see cref="IRateLimitCounterStore"/> (<see cref="CacheRateLimitCounterStore"/>),
    /// settings validation, and <see cref="DistributedRateLimitingMiddleware"/>.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Application configuration used to bind <c>RateLimitingSettings</c>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// <c>StangaNetLib.Caching</c> must be registered before calling this method
    /// (<c>services.AddStangaNetLibCaching(...)</c>).
    /// </remarks>
    public static IServiceCollection AddStangaNetLibRateLimit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitingSettings>(
            configuration.GetSection(RateLimitingSettings.SectionName));

        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<RateLimitingSettings>,
            RateLimitingSettingsValidator>();

        services.AddSingleton<IRateLimitCounterStore, CacheRateLimitCounterStore>();
        services.AddSingleton<IRateLimitService, RateLimitService>();

        return services;
    }

    /// <summary>
    /// Replaces the default <see cref="IRateLimitCounterStore"/> with a custom implementation.
    /// Use this to plug in an atomic Redis INCR store for strict enforcement under high concurrency.
    /// </summary>
    /// <typeparam name="TStore">Custom store type implementing <see cref="IRateLimitCounterStore"/>.</typeparam>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStangaNetLibRateLimitStore<TStore>(
        this IServiceCollection services)
        where TStore : class, IRateLimitCounterStore
    {
        var descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IRateLimitCounterStore));
        if (descriptor is not null)
            services.Remove(descriptor);

        services.AddSingleton<IRateLimitCounterStore, TStore>();
        return services;
    }

    /// <summary>
    /// Adds <see cref="DistributedRateLimitingMiddleware"/> to the pipeline.
    /// Must be placed after <c>UseRouting()</c> so that endpoint metadata
    /// (and therefore <c>[RateLimit]</c> attributes) are available.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UseStangaNetLibRateLimit(this IApplicationBuilder app)
    {
        app.UseMiddleware<DistributedRateLimitingMiddleware>();
        return app;
    }

    /// <summary>
    /// Policy name for the auth lockout rate-limit preset. Use with <c>[RateLimit("AuthLockout")]</c>.
    /// </summary>
    public const string AuthLockoutPolicyName = "AuthLockout";

    /// <summary>
    /// Registers the <c>AuthLockout</c> rate-limit preset (default: 5 requests / 15 minutes / IP).
    /// This is an endpoint rate limit for failed-login surfaces; account-level lockout belongs in
    /// Auth <c>ILoginAttemptService</c> — combine both for defence in depth.
    /// Call after <see cref="AddStangaNetLibRateLimit"/>.
    /// </summary>
    public static IServiceCollection AddStangaNetLibAuthLockoutRateLimit(
        this IServiceCollection services,
        int limit = 5,
        int windowSeconds = 900)
    {
        services.PostConfigure<RateLimitingSettings>(settings =>
        {
            if (settings.Policies.Any(p => p.Name.Equals(AuthLockoutPolicyName, StringComparison.OrdinalIgnoreCase)))
                return;

            settings.Policies.Add(new RateLimitPolicy
            {
                Name = AuthLockoutPolicyName,
                Limit = limit,
                WindowSeconds = windowSeconds,
                Scope = RateLimitScope.IP,
            });
        });

        return services;
    }
}
