---
name: smoke-test
description: Build, unit-test, and run VSMarketplaceBadges locally, then request a real badge URL end-to-end to confirm it renders against the live Marketplace and shields.io. Use to verify a change that touches request handling, the Marketplace client, or output formatting.
---

The unit tests cover badge value formatting and route binding, but nothing exercises the real HTTP path.
Prove end-to-end changes by serving an actual badge. Takes an optional badge path as `$ARGUMENTS`
(e.g. `version/ms-dotnettools.csharp.svg`).

## 1. Build and unit-test

```
dotnet test
```

If this fails, stop here — no need to launch the app.

## 2. Run in the background

```
ASPNETCORE_ENVIRONMENT=Development dotnet run --project VSMarketplaceBadges.csproj
```

Use `run_in_background`, then wait for `Now listening on:` with an `until grep -q` loop on the task's
output file rather than sleeping.

`launchSettings.json` is gitignored, so only **http://localhost:5000** is bound — there is no HTTPS port
and `UseHttpsRedirection` no-ops as a result. Do not try `https://localhost:5001`; it refuses the
connection.

## 3. Request a badge

```
curl -sS -o badge.svg -w '%{http_code} %{content_type} %{size_download}B\n' \
  http://localhost:5000/version/ms-dotnettools.csharp.svg
grep -o '>[^<]*</text>' badge.svg | tail -1     # the rendered label
```

Check all three:
- status `200` (a `400` means the badge type or extension did not bind — check the `EnumMember` value)
- content type `image/svg+xml` (or `image/png` for `.png`)
- the rendered label is the real value, not `unknown`. A grey `unknown` badge means the Marketplace
  lookup returned nothing — verify the extension id, then the `ToBadgeValue` case.

Cover a `.svg`, a `.png`, and one deliberately invalid segment (e.g. `bogus-type/...`, which must still
return 400) when the change touches formatting, content types, or binding.

## 4. Stop the server

Kill the background task when finished. Do not leave it running.

## Note

The distributed cache is unwired (see CLAUDE.md), so every request hits the live Marketplace and
shields.io APIs. `Redis error.` entries in the log are expected and are not a smoke-test failure.
