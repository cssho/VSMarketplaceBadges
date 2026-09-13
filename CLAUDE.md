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

- ターゲットは `net10.0` (LTS、2028年11月まで)。リポジトリルートに `VSMarketplaceBadges.sln` と
  `VSMarketplaceBadges.csproj` の両方があるため、Web プロジェクトだけを対象にするコマンドでは
  明示的にプロジェクトを指定する必要がある (`dotnet publish VSMarketplaceBadges.csproj`)。
  指定しないと MSBuild が曖昧さでエラーになる。
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

シンクは環境によらず stdout の 1 本だけ。Lambda は stdout をそのまま
CloudWatch Logs に転送するため、アプリ側は AWS SDK を持たない。保持期間とコストは
CloudWatch のロググループ側 (`terraform/main.tf`) で制御する。

`SelfLog` は Serilog 内部の障害を stderr に出力する。フォーマッタ例外などを黙って
握りつぶさないために入れている。

## 上流障害時のフォールバック

`BadgeController` は Marketplace API と shields.io の呼び出しをそれぞれ try で囲み、
どちらが落ちても `wwwroot/unavailable.{svg,png}` を 200 で返す。README に貼られたバッジが
壊れた画像アイコンにならないようにするため。同梱物すら読めないときだけ 503。

- 同梱バッジは shields.io で実際に生成したものをそのまま置いてある (見た目を本物と揃えるため)。
  差し替えるときは `https://img.shields.io/badge/VS%20Marketplace-unavailable-lightgrey.{svg,png}` から取り直す。
- `FallbackBadgeService` は Singleton で、起動時に一度だけ wwwroot から読んでメモリに持つ。
  フォールバックが要る場面は上流障害中なので、そこでディスクを触らせない。
- フォールバック時は `Cache-Control` を 60 秒に上書きする。既定の 3600 秒のままだと、
  上流が復旧しても最大 1 時間 CloudFront に unavailable バッジが残り続ける。
- `ToBadgeValue` は意図的に try の外に置いてある。ここでの例外は自前ロジックの不具合なので、
  フォールバックで覆い隠さず 500 として表に出す。

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
  ポリシーは `RetryPolicy()` / `PerAttemptTimeoutPolicy()` に切り出してあるので、両クライアントで共有すること。
- コメント (`//` と XML ドキュメントコメントの両方) は日本語で書く。

## Native AOT は採用しない

.NET 10 移行時に検討したが見送った。**ASP.NET Core MVC は Native AOT に非対応**で、
[対応しているのは Minimal API のみ](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)。
このアプリは `AddControllers` / `[FromRoute]` / `[FromQuery]` のモデルバインド /
`ImageOutputFormatter : OutputFormatter` / `ObjectResult` と MVC に深く依存しているため、
AOT 化はリクエスト処理層の全面書き換えになる。

得られるのはコールドスタートの短縮だが、バッジは CloudFront に 1 時間キャッシュされ
(ヒット時 20〜30ms)、コールドスタートに当たる利用者は稀。Lambda の実行費用も無料枠内で $0。
SnapStart を切ったのと同じ理由で、書き換えのリスクに見合わない。

## SnapStart は意図的に無効

一度有効にして実測したうえで切った。理由はコスト:

- 効果はコールドスタート 2405ms → 1923ms (**-482ms**) にとどまった。SnapStart は初期化の
  やり直しを省くだけで、.NET のティアード コンパイルまでは肩代わりしない。
- 費用は**スナップショット 1 本 (1024MB) あたり月 $3.90**。SnapStart は発行済みバージョン
  ごとに保管料がかかるため、デプロイのたびに積み上がる。5 バージョンで月 $19.50 相当。
- 実行分 (GB-秒・リクエスト) は無料枠内で $0。つまり Lambda の費用はほぼ全額が保管料だった。
- バッジは CloudFront に 1 時間キャッシュされ、コールドスタートに当たる利用者は稀
  (キャッシュヒット時は 20〜30ms)。482ms のために月 $4〜20 は見合わない。

再検討するなら、`deploy-lambda.yml` のバージョン掃除が効いていることと、メモリを下げて
スナップショットを小さくすることを併せて評価すること。

## タイムアウトの予算 (3 か所が連動)

Lambda では待機時間がそのまま課金される。以下は 1 つだけ変えてはいけない:

| 層 | 値 | 場所 |
| --- | --- | --- |
| 外部 HTTP 1 呼び出し (ハードキャップ) | 10 秒 | `Startup.cs` の `TotalTimeoutPolicy` |
| 1 リクエスト最悪 | 30 秒 | Marketplace 10×2 + shields.io 10 |
| Lambda | 35 秒 | `terraform/variables.tf` の `lambda_timeout` |
| CloudFront オリジン応答 | 40 秒 | `terraform/variables.tf` の `origin_read_timeout` |

`VSMarketplaceService.LoadVsmItemDataFromApi` は失敗時に `CoreRequest` をもう一度呼ぶため、
Marketplace 側は shields.io 側の 2 倍かかる。予算を計算するときはこれを忘れないこと。

Polly のポリシーは `TotalTimeoutPolicy` → `RetryPolicy` → `PerAttemptTimeoutPolicy` の順に
登録して外側から重ねる。`PerAttemptTimeoutPolicy` が投げる `TimeoutRejectedException` は
`HttpRequestException` ではないので、`RetryPolicy` の `.Or<TimeoutRejectedException>()` を
外すとタイムアウトがリトライされずそのまま 500 になる。

## デプロイ

インフラは `terraform/` に Terraform で定義してある。手順・フラグ・ロールバックは
`terraform/README.md` を参照。

配信経路は `vsmarketplacebadges.dev` (Route 53) → CloudFront → Lambda。DNS は Gandi から
Route 53 へ移管済みで、apex は CloudFront への ALIAS。

**変数の既定値が稼働中の状態そのもの**なので、`terraform plan` は差分ゼロが正常。
`enable_custom_domain` / `manage_dns` は段階移行のために用意したフラグで、**false に倒すと
証明書とホストゾーンが消えてバッジ配信もメールも止まる**。plan に
`aws_route53_zone.main[0] will be destroyed` が出たら apply しないこと。

切り戻しは **Lambda エイリアス (`live`) の版戻し**で行う。

```
aws lambda update-alias --function-name vsmarketplace-badges --name live --function-version <前の版>
```

即時に反映される。`deploy-lambda.yml` が直近 3 世代を残すので 1〜2 世代前まで戻せる。

- 作業はフィーチャーブランチで行い、PR を作成する。`master` へ直接コミットしないこと。
- `master` への push は 2 つのワークフローを同時に起動する。**`master` へのマージは本番デプロイに等しい。**
  - `.github/workflows/deploy-lambda.yml` — 新しい配信経路。publish → ZIP →
    `update-function-code` → バージョン発行 → `live` エイリアス付け替え → CloudFront 無効化。
    認証は **OIDC** (`vars.AWS_DEPLOY_ROLE_ARN`)。長期アクセスキーは使わないので、
    `permissions: id-token: write` を消すとロールを引き受けられなくなる。
- CloudFront は Lambda の **`live` エイリアス**を向いている。`$LATEST` は公開経路ではなく、
  SnapStart も効かない。デプロイでエイリアスを付け替えるのを飛ばすと、コードを更新しても
  配信内容が変わらない。
- 認証は **OIDC** のみ。長期アクセスキーは使わない (App Runner 撤去時に IAM ユーザーごと削除済み)。

