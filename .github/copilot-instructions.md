# StangaNetLib.RequestProtection — Copilot instructions

- ASP.NET Core edge protection: distributed rate limiting + idempotency, both via `StangaNetLib.Caching.Storage.ICacheService`.
- Prefer `Result` / `Result<T>` and `Guard` from `StangaNetLib.Core` — unwrap cache Results explicitly.
- Keep public APIs documented (`///`) and options-driven (`RateLimitingSettings`, `IdempotencyOptions`).
- DI: `AddStangaNetLibRateLimit` / `AddStangaNetLibIdempotency` (Caching must be registered first).
- Pin `StangaNetLib.Caching` ≥ 1.0.1; never reintroduce ME 10.x.
- Match existing naming, file-scoped namespaces, and EditorConfig / Roslyn conventions.
- Tests use xUnit v3 + FluentAssertions; cover `net8.0`, `net9.0`, and `net10.0` when changing shared behavior.
