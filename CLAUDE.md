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

シンクは環境によらず stdout の 1 本だけ。Lambda / App Runner とも stdout をそのまま
CloudWatch Logs に転送するため、アプリ側は AWS SDK を持たない。保持期間とコストは
CloudWatch のロググループ側 (`terraform/main.tf`) で制御する。

以前は Production のみ Amazon S3 (`vsmarketplace-badges/logs`) に送っていたが、Lambda 移行に
伴い廃止した (`Serilog.Sinks.AmazonS3` 参照ごと削除)。

`SelfLog` は Serilog 内部の障害を stderr に出力する。stdout シンクのみになった現在も、
フォーマッタ例外などを黙って握りつぶさないために残している。

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

## デプロイ (移行期間中は 2 系統が並走)

インフラは `terraform/` に Terraform で定義してある。手順・フラグ・ロールバックは
`terraform/README.md` を参照。

配信経路は `vsmarketplacebadges.dev` (Route 53) → CloudFront → Lambda。DNS は Gandi から
Route 53 へ移管済みで、apex は CloudFront への ALIAS。

**変数の既定値が稼働中の状態そのもの**なので、`terraform plan` は差分ゼロが正常。
`enable_custom_domain` / `manage_dns` は段階移行のために用意したフラグで、**false に倒すと
証明書とホストゾーンが消えてバッジ配信もメールも止まる**。plan に
`aws_route53_zone.main[0] will be destroyed` が出たら apply しないこと。

切り戻しは DNS ではなく CloudFront のオリジン切替 (`rollback_to_apprunner`) で行う。
apex を App Runner に戻す DNS 手段が無いため (Route 53 の ALIAS 対象は 2022-08-01 以降に
作成されたサービスのみ、当該サービスは 2022-05-06 作成)。

- 作業はフィーチャーブランチで行い、PR を作成する。`master` へ直接コミットしないこと。
- `master` への push は 2 つのワークフローを同時に起動する。**`master` へのマージは本番デプロイに等しい。**
  - `.github/workflows/deploy-lambda.yml` — 新しい配信経路。publish → ZIP →
    `update-function-code` → バージョン発行 → `live` エイリアス付け替え → CloudFront 無効化。
    認証は **OIDC** (`vars.AWS_DEPLOY_ROLE_ARN`)。長期アクセスキーは使わないので、
    `permissions: id-token: write` を消すとロールを引き受けられなくなる。
  - `.github/workflows/push-ecr.yml` — 旧経路 (App Runner)。ECR への push までは成功するが、
    **App Runner のデプロイは必ずロールバックする** (下記)。こちらは今も長期アクセスキー
    (`secrets.AWS_ACCESS_KEY_ID`) を使う。撤去時に鍵ごと消す。
- CloudFront は Lambda の **`live` エイリアス**を向いている。`$LATEST` は公開経路ではなく、
  SnapStart も効かない。デプロイでエイリアスを付け替えるのを飛ばすと、コードを更新しても
  配信内容が変わらない。
- DNS 切り替えが定着したら `push-ecr.yml` / `Dockerfile` / ECR / App Runner を撤去する。

### App Runner は .NET 8 のコードをデプロイできない (対処しないと決めた既知の状態)

App Runner サービスは**ポート 80** を待ち受ける設定だが、`mcr.microsoft.com/dotnet/aspnet:8.0`
の既定ポートは **8080** (.NET 8 で 80 から変更された)。コンテナは 8080 で listen するため
TCP ヘルスチェックが通らず、デプロイは毎回 `ROLLBACK_SUCCEEDED` で 2022 年のイメージに戻る。

その結果:

- **App Runner が配信しているのは 2022 年のコード**。.NET 8 移行も semver 修正も
  フォールバックバッジも入っていない。バージョンバッジが `v2.23.2` を返すのは
  序数比較のままだから (正しくは `v2.151.28`)。
- したがって **App Runner は「動くが 4 年前の挙動」の切り戻し先**でしかない。
  `terraform/README.md` のロールバック手順はこの前提で読むこと。
- Lambda 経路は HTTP ポートを使わない (ランタイム API 経由) ため影響を受けない。

直すなら `Dockerfile` に `ENV ASPNETCORE_HTTP_PORTS=80` を足すだけだが、App Runner は
まもなく撤去するため**意図的に対処しない**と判断した。`master` への push で
`Push Amazon ECR` が緑になり App Runner がロールバックするのは想定内であり、調査不要。
