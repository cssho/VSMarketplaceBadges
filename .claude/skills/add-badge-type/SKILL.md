---
name: add-badge-type
description: Add a new badge type to VSMarketplaceBadges. Walks the four coordinated edits (BadgeType enum, BadgeRequest subject, ToBadgeValue case, doc page) that a new badge requires. Use when asked to add, rename, or remove a badge type.
---

Adding a badge type touches four files. Skipping any one produces a badge that 400s or throws at
request time instead of failing at build time — so make all four edits before reporting done.

If the user did not say which badge type, ask for: the URL segment (kebab-case, e.g. `trending-yearly`),
the shields.io subject label, and how the value is computed from `VSMarketplaceItem`.

## 1. `Entity/BadgeType.cs`

Add an enum member with `[EnumMember(Value = "<url-segment>")]`. `CustomEnumConverter` builds its lookup
from these attribute values, so a member without one binds to `BadgeType.Unknown` and the controller
returns 400. Keep `Unknown` first.

## 2. `Entity/BadgeRequest.cs`

Add a `private const string <Name>Subject = "...";` and a case in the `BadgeType` property setter's
switch expression. Subject strings are URL-encoded literals — spaces are `%20`, and a literal hyphen in
a shields.io label is doubled (`trending--daily`).

## 3. `Utility/BadgeValuConverterExtentions.cs`

Add a case to `ToBadgeValue`. The `default` branch throws `ArgumentException`, so an omitted case is a
runtime 500. Reuse the existing helpers where they fit:
- `ApplyUnit(...)` for the K/M/G abbreviated variants
- `Math.Round(x, 2)` for rating and trending values

If the value needs a new field from the Marketplace API, add it to `Entity/VSMarketplaceItem.cs` and map
it in the constructor's `statistic.StatisticName` switch (names are the API's, e.g. `trendingdaily`).

## 4. `wwwroot/index.html`

Add a row to the public doc page listing the new URL segment and a live example, matching the format of
the surrounding rows.

## Verify

```
dotnet test
```

`BadgeTypeBindingTests` enumerates every `BadgeType` and fails if the `EnumMember`, the subject, or the
`ToBadgeValue` case is missing — so steps 1–3 are checked automatically. Add a formatting assertion to
`BadgeValueConverterTests` for the new value, then run `/smoke-test` against the new segment before
reporting the change complete.
