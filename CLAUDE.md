# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

回答は日本語で行うこと。

## Overview

ASP.NET Core web service that serves shields.io badges for Visual Studio Marketplace extensions.
Route shape: `GET /{badgeType}/{itemName}.{svg|png}` — see `wwwroot/index.html` for the public doc page.

## Build & test

```
dotnet build                                      # solution: web project + tests
dotnet test                                       # 83 unit tests
dotnet test --filter FullyQualifiedName~RatingStar
dotnet watch run --project VSMarketplaceBadges.csproj   # local dev
```

- Targets `net8.0` (LTS). The repo root holds both `VSMarketplaceBadges.sln` and
  `VSMarketplaceBadges.csproj`, so any command that must act on the web project alone needs it named
  explicitly (`dotnet publish VSMarketplaceBadges.csproj`) — otherwise MSBuild errors on the ambiguity.
  The Dockerfile already does this.
- The web project's root **is** the repo root, so its default `**/*.cs` glob would swallow the test
  project. `VSMarketplaceBadges.csproj` carries `<Compile Remove="tests/**" />` to prevent that; keep it.
- `launchSettings.json` is gitignored, so `dotnet run` binds **http://localhost:5000 only** — there is no
  HTTPS port locally, and `UseHttpsRedirection` no-ops as a result.
- The dotnet CLI on this machine emits Japanese output (`ビルドに成功しました。` = build succeeded).

## Load-bearing misspellings

These typos are baked into namespaces, file names, and DI registrations. Match them exactly; do not
"correct" them in passing, since a rename touches every reference:

- `Shileds` (not `Shields`) — `IShiledsIoService`, `ShiledsIoService`
- `Midlewares/` directory holds the `VSMarketplaceBadges.Middlewares` namespace (dir/namespace differ)
- `BadgeValuConverterExtentions.cs`

## Logging

Serilog is configured by hand in `Program.cs`, **not** from `appsettings.json` — the
`Logging:LogLevel` section that ASP.NET Core normally uses is inert here. The knob is
`Serilog:MinimumLevel` (or the `Serilog__MinimumLevel` environment variable), defaulting to `Debug` in
Development and `Information` elsewhere.

Sinks are selected by `ASPNETCORE_ENVIRONMENT`: Development → console, Production → Amazon S3
(`vsmarketplace-badges/logs`, ap-northeast-1). Any other value gets **no sink at all**.

`SelfLog` writes sink failures to stderr. Without it, a batching sink swallows S3 errors and the app
keeps serving badges while log shipping is silently dead.

## Distributed cache is intentionally unwired

`UseCacheService` injects `IDistributedCache`, but no distributed cache is registered in `Startup.cs`.
Redis was removed. Every cache call therefore fails and is swallowed with a `logger.LogError(e, "Redis error.")`.
This is the known current state, not a bug to fix. Cache-related error logs in production are expected noise.

## Adding a badge type

A new badge type requires four coordinated edits; missing any one produces a silently broken badge:

1. `Entity/BadgeType.cs` — enum member with a matching `[EnumMember(Value="kebab-case")]`.
   Route binding goes through `CustomEnumConverter`, which keys off `EnumMember`, and falls back to
   `BadgeType.Unknown` (→ 400) when the string does not match.
2. `Entity/BadgeRequest.cs` — a subject constant plus a case in the `BadgeType` setter switch.
   Subjects are URL-encoded literals (e.g. `Visual%20Studio%20Marketplace`).
3. `Utility/BadgeValuConverterExtentions.cs` — a case in `ToBadgeValue`; the `default` branch throws.
4. `wwwroot/index.html` — a row on the public doc page.

`BadgeTypeBindingTests` enumerates every `BadgeType` and fails if steps 1–3 are incomplete, so run
`dotnet test` after adding one. Use the `/add-badge-type` skill for this.

## Conventions

- Private fields are lowercase without an underscore prefix (`private readonly ILogger logger;`).
- Outbound HTTP goes through typed `HttpClient`s registered in `Startup.cs` with Polly retry/timeout
  policies. Add new external calls the same way rather than newing up an `HttpClient`.

## Repo etiquette

- Work on a feature branch and open a PR; do not commit directly to `master`.
- Pushing to `master` triggers `.github/workflows/push-ecr.yml`, which builds the Dockerfile and pushes
  `:latest` to Amazon ECR (ap-northeast-1). A merge to `master` is a production deploy.
