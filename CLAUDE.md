# CLAUDE.md

このファイルは、Claude Code (claude.ai/code) がこのリポジトリで作業する際のガイドです。

回答は日本語で行うこと。CLAUDE.md およびソースコード中のコメントも日本語で記述すること。

## 概要

Visual Studio Marketplace 拡張機能向けに shields.io バッジを配信する ASP.NET Core の Web サービス。
ルート形式: `GET /{badgeType}/{itemName}.{svg|png}` — 公開ドキュメントページは `wwwroot/index.html`。

## ビルドとテスト

```
dotnet build                                      # ソリューション: Web プロジェクト + テスト
dotnet test                                       # ユニットテスト 91 件
dotnet test --filter FullyQualifiedName~RatingStar
dotnet watch run --project VSMarketplaceBadges.csproj   # ローカル開発
```

- ターゲットは `net8.0` (LTS)。リポジトリルートに `VSMarketplaceBadges.sln` と
  `VSMarketplaceBadges.csproj` の両方があるため、Web プロジェクトだけを対象にするコマンドでは
  明示的にプロジェクトを指定する必要がある (`dotnet publish VSMarketplaceBadges.csproj`)。
  指定しないと MSBuild が曖昧さでエラーになる。Dockerfile は既に指定済み。
- Web プロジェクトのルートが**リポジトリルートそのもの**なので、既定の `**/*.cs` グロブは
  テストプロジェクトまで巻き込んでしまう。`VSMarketplaceBadges.csproj` の
  `<Compile Remove="tests/**" />` がそれを防いでいるので、削除しないこと。
- `launchSettings.json` は gitignore されているため、`dotnet run` は
  **http://localhost:5000 のみ**にバインドする。ローカルに HTTPS ポートはなく、
  結果として `UseHttpsRedirection` は何もしない。
- このマシンの dotnet CLI は日本語で出力する (`ビルドに成功しました。`)。

## バージョンの比較

`VSMarketplaceItem` が表示するバージョンは `Utility/SemanticVersionComparer.cs` で選ぶ。
semver の優先順位規則 (数値部はセグメントごとの数値比較、プレリリース < 正式版、ビルドメタデータは無視) に従い、
数値部が semver として解釈できないバージョンが混ざった場合のみ序数の文字列比較にフォールバックする。

## 修正してはいけないスペルミス

これらのタイプミスは名前空間・ファイル名・DI 登録に組み込まれている。リネームすると全参照に
波及するため、ついでに「修正」せず、そのまま正確に合わせること:

- `Shileds` (`Shields` ではない) — `IShiledsIoService`, `ShiledsIoService`
- `Midlewares/` ディレクトリに `VSMarketplaceBadges.Middlewares` 名前空間が入っている
  (ディレクトリ名と名前空間が異なる)
- `BadgeValuConverterExtentions.cs`

## ロギング

Serilog は `appsettings.json` からではなく `Program.cs` で手書き設定している。そのため
ASP.NET Core が通常使う `Logging:LogLevel` セクションはここでは無効。設定項目は
`Serilog:MinimumLevel` (または環境変数 `Serilog__MinimumLevel`) で、既定値は Development で
`Debug`、それ以外では `Information`。

シンクは `ASPNETCORE_ENVIRONMENT` で選択される: Development → コンソール、Production → Amazon S3
(`vsmarketplace-badges/logs`, ap-northeast-1)。それ以外の値では**シンクが一切設定されない**。

`SelfLog` はシンクの障害を stderr に出力する。これがないとバッチングシンクが S3 のエラーを
握りつぶし、ログ転送が死んだままバッジ配信だけが続いてしまう。

## 分散キャッシュは意図的に未接続

`UseCacheService` は `IDistributedCache` を注入しているが、`Startup.cs` では分散キャッシュを
一切登録していない。Redis は削除済み。したがってすべてのキャッシュ呼び出しは失敗し、
`logger.LogError(e, "Redis error.")` で握りつぶされる。
これは既知の現状であり、修正すべきバグではない。本番でのキャッシュ関連エラーログは想定内のノイズ。

## バッジタイプの追加

新しいバッジタイプには 4 箇所の連動した編集が必要。1 つでも欠けるとバッジが静かに壊れる:

1. `Entity/BadgeType.cs` — `[EnumMember(Value="kebab-case")]` を付けた enum メンバー。
   ルートバインドは `CustomEnumConverter` を経由し、`EnumMember` をキーにする。文字列が
   一致しない場合は `BadgeType.Unknown` (→ 400) にフォールバックする。
2. `Entity/BadgeRequest.cs` — subject 定数と、`BadgeType` セッターの switch への case 追加。
   subject は URL エンコード済みのリテラル (例: `Visual%20Studio%20Marketplace`)。
3. `Utility/BadgeValuConverterExtentions.cs` — `ToBadgeValue` への case 追加。`default` は例外を投げる。
4. `wwwroot/index.html` — 公開ドキュメントページへの行追加。

`BadgeTypeBindingTests` が全 `BadgeType` を列挙し、手順 1〜3 が不完全なら失敗する。追加後は
必ず `dotnet test` を実行すること。この作業には `/add-badge-type` スキルを使う。

## コーディング規約

- private フィールドはアンダースコア接頭辞なしの小文字始まり (`private readonly ILogger logger;`)。
- 外部への HTTP は `Startup.cs` で Polly のリトライ/タイムアウトポリシー付きに登録した
  型付き `HttpClient` を経由する。新しい外部呼び出しも `HttpClient` を直接 new せず同じ方式で追加する。
- コメント (`//` と XML ドキュメントコメントの両方) は日本語で書く。

## リポジトリ運用ルール

- 作業はフィーチャーブランチで行い、PR を作成する。`master` へ直接コミットしないこと。
- `master` への push は `.github/workflows/push-ecr.yml` を起動し、Dockerfile をビルドして
  `:latest` を Amazon ECR (ap-northeast-1) に push する。`master` へのマージは本番デプロイに等しい。
