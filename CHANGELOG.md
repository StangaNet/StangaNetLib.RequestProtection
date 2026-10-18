# Changelog

All notable changes to **StangaNetLib.RequestProtection** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-10-18

### Added
- Distributed fixed-window rate limiting (`IRateLimitService`, `[RateLimit]`, middleware) backed by `ICacheService`.
- Idempotency middleware with response replay, in-flight 409 collision detection, and user/IP key scoping.
- DI helpers: `AddStangaNetLibRateLimit` / `UseStangaNetLibRateLimit`, `AddStangaNetLibIdempotency` / `UseStangaNetLibIdempotency`.
- Dual targeting `net8.0` / `net9.0`, XML docs, symbols (`snupkg`), package icon, and MSBuild consumer metadata stamp.
- Depends on `StangaNetLib.Core` 1.0.0 and `StangaNetLib.Caching` 1.0.1; Microsoft.Extensions.* pinned to 8.x.
- CI via shared StangaNet reusable workflow (push / PR / manual; publish outside PRs).

[Unreleased]: https://github.com/StangaNet/StangaNetLib.RequestProtection/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/StangaNet/StangaNetLib.RequestProtection/releases/tag/v1.0.0
