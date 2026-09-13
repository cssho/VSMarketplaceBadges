# VSMarketplaceBadges

Visual Studio Marketplace 拡張機能向けのバッジ配信サービス。

拡張機能のバージョン・インストール数・評価などを [shields.io](https://shields.io) 形式の
バッジ画像として返します。README に画像として貼るだけで使えます。

**https://vsmarketplacebadges.dev**

## 使い方

```
https://vsmarketplacebadges.dev/{バッジ種別}/{発行者}.{拡張機能名}.{svg|png}
```

`{発行者}.{拡張機能名}` は Marketplace の URL の `itemName` と同じ値です。

```markdown
[![Version](https://vsmarketplacebadges.dev/version-short/ms-dotnettools.csharp.svg)](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp)
```

## バッジ種別

| 種別 | 表示例 |
| --- | --- |
| `version` | `Visual Studio Marketplace \| v2.151.28` |
| `version-short` | `VS Marketplace \| v2.151.28` |
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

拡張子は `.svg` と `.png` が使えます。README に貼るなら `.svg` を推奨します。

## 見た目のカスタマイズ

shields.io のパラメータをクエリ文字列で渡せます。

```
https://vsmarketplacebadges.dev/version-short/ms-dotnettools.csharp.svg?color=blue&style=flat-square
```

対応するパラメータ:

`color` `label` `labelColor` `link` `logo` `logoColor` `logoSize` `logoWidth` `style` `subject`

`subject` はバッジ左側のラベルを差し替えます (`label` と同じ用途)。
ここに無いパラメータは無視されます。

## 挙動

- レスポンスは 1 時間キャッシュされます。拡張機能を更新しても反映まで最大 1 時間かかります。
- 存在しない拡張機能を指定すると `unknown` と表示されたバッジを返します (エラーにはなりません)。
- 上流 (Marketplace API / shields.io) に障害があるときは `unavailable` バッジを返します。
- 未知のバッジ種別や拡張子は `400` を返します。

## 開発

.NET 10 SDK が必要です。

```bash
dotnet build                                            # ソリューション全体
dotnet test                                             # ユニットテスト
dotnet watch run --project VSMarketplaceBadges.csproj   # http://localhost:5000
```

リポジトリルートに `.sln` と `.csproj` の両方があるため、Web プロジェクトだけを対象にする
コマンドではプロジェクトを明示してください (`dotnet publish VSMarketplaceBadges.csproj`)。

バッジ種別の追加手順や実装上の注意は [CLAUDE.md](CLAUDE.md) にまとめています。

## 構成

```
vsmarketplacebadges.dev (Route 53)
  └─ CloudFront ─ Lambda (.NET 10) ─┬─ Marketplace API
                                    └─ shields.io
```

`master` へのマージで GitHub Actions が Lambda にデプロイします。インフラは Terraform で
管理しており、詳細は [terraform/README.md](terraform/README.md) を参照してください。

## ライセンス

[MIT](LICENSE)
