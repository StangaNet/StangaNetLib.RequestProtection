# StangaNetLib.RequestProtection

[![.NET CI](https://github.com/StangaNet/StangaNetLib.RequestProtection/actions/workflows/main.yml/badge.svg)](https://github.com/StangaNet/StangaNetLib.RequestProtection/actions/workflows/main.yml)
![NuGet](https://img.shields.io/badge/nuget-1.0.0-blue)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4)

HTTP request protection for ASP.NET Core: distributed fixed-window rate limiting and idempotency deduplication, both backed by [`StangaNetLib.Caching`](https://github.com/StangaNet/StangaNetLib.Caching) (`ICacheService`).

## Installation

The package is hosted on **GitHub Packages** (pulls Core/Caching from the same feed).

```xml
<!-- NuGet.config — source key must match your packageSourceCredentials -->
<configuration>
  <packageSources>
    <add key="StangaNet" value="https://nuget.pkg.github.com/StangaNet/index.json" />
  </packageSources>
</configuration>
```

```xml
<!-- .csproj -->
<PackageReference Include="StangaNetLib.RequestProtection" Version="1.0.0" />
```

Requires `StangaNetLib.Caching` **≥ 1.0.1**. See [CHANGELOG.md](./CHANGELOG.md).

## Core Components

| Area | Components | Purpose |
|---|---|---|
| Rate limit | `IRateLimitService`, `[RateLimit]`, `DistributedRateLimitingMiddleware` | Fixed-window limits |
| Idempotency | `IdempotencyMiddleware`, `IdempotencyOptions` | Dedup + replay |
| Extensions | `AddStangaNetLibRateLimit`, `AddStangaNetLibIdempotency` | DI / pipeline |

## Quick Start

```csharp
builder.Services.AddStangaNetLibCaching(...); // required first
builder.Services.AddStangaNetLibRateLimit(builder.Configuration);
builder.Services.AddStangaNetLibIdempotency(builder.Configuration);

app.UseStangaNetLibRateLimit();
app.UseStangaNetLibIdempotency();
```

## License

Apache-2.0 — see [LICENSE](./LICENSE) and [NOTICE](./NOTICE).
