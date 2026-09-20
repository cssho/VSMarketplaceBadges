# VSMarketplaceBadges

*[日本語](README.ja.md)*

Badges for Visual Studio Marketplace extensions.

Serves an extension's version, install count, rating and other stats as a
[shields.io](https://shields.io)-style badge image. Drop it into a README as an image and you're done.

**https://vsmarketplacebadges.dev**

## Usage

```
https://vsmarketplacebadges.dev/{badge type}/{publisher}.{extension}.{svg|png}
```

`{publisher}.{extension}` is the `itemName` from the extension's Marketplace URL.

```markdown
[![Version](https://vsmarketplacebadges.dev/version-short/ms-dotnettools.csharp.svg)](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp)
```

## Badge types

| Type | Renders as |
| --- | --- |
| `version` | `Visual Studio Marketplace \| v2.160.4` |
| `version-short` | `VS Marketplace \| v2.160.4` |
| `installs` | `installs \| 236364283` |
| `installs-short` | `installs \| 236M` |
| `downloads` | `downloads \| 290992013` |
| `downloads-short` | `downloads \| 290M` |
| `rating` | `rating \| 4.5/5 (1234)` |
| `rating-short` | `rating \| 4.5/5` |
| `rating-star` | `rating \| ★★★★½` |
| `trending-daily` | `trending--daily \| 12` |
| `trending-weekly` | `trending--weekly \| 34` |
| `trending-monthly` | `trending--monthly \| 56` |

Both `.svg` and `.png` work. Prefer `.svg` for READMEs.

## Customizing the look

shields.io parameters are passed through as query strings.

```
https://vsmarketplacebadges.dev/version-short/ms-dotnettools.csharp.svg?color=blue&style=flat-square
```

Supported parameters:

`color` `label` `labelColor` `link` `logo` `logoColor` `logoSize` `logoWidth` `style` `subject`

`subject` overrides the left-hand label (same purpose as `label`).
Anything not on this list is ignored.

## Behavior

- Responses are cached for one hour. Expect up to an hour before an extension update shows up.
- An unknown extension renders a badge reading `unknown` rather than returning an error.
- If an upstream (Marketplace API or shields.io) is down, a bundled `unavailable` badge is served.
- An unknown badge type or file extension returns `400`.

## Development

Requires the .NET 10 SDK.

```bash
dotnet build                                            # whole solution
dotnet test                                             # unit tests
dotnet watch run --project VSMarketplaceBadges.csproj   # http://localhost:5000
```

The repository root holds both the `.sln` and the `.csproj`, so name the project explicitly for
commands that should target only the web project (`dotnet publish VSMarketplaceBadges.csproj`).

How to add a badge type, plus the implementation gotchas worth knowing, are in
[CLAUDE.md](CLAUDE.md) (Japanese).

## Architecture

```
vsmarketplacebadges.dev (Route 53)
  └─ CloudFront ─ Lambda (.NET 10) ─┬─ Marketplace API
                                    └─ shields.io
```

Merging to `master` deploys to Lambda via GitHub Actions. Infrastructure is managed with
Terraform — see [terraform/README.md](terraform/README.md) (Japanese) for details.

## Contributing

Issues and pull requests are welcome.

Found a security issue? Please report it through
[private vulnerability reporting](https://github.com/cssho/VSMarketplaceBadges/security/advisories/new)
rather than a public issue.

## License

[MIT](LICENSE)
