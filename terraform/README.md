# インフラ (App Runner → Lambda + CloudFront)

App Runner がメンテナンスモードに入った (2026/4/30 以降は新規顧客の受付停止、新機能追加なし)
ことと、常時起動コンテナの月額を落とすことを目的に、配信基盤を Lambda + CloudFront へ移す。

## 構成

```
Route 53 (apex: vsmarketplacebadges.dev)
  └─ CloudFront                         ACM 証明書は us-east-1
       ├─ Cache Policy: クエリ文字列を全てキャッシュキーに含める ★
       ├─ TTL 3600 (オリジンの [ResponseCache(Duration = 3600)] に合わせる)
       └─ Origin: Lambda Function URL (AWS_IAM + OAC で直叩きを封鎖)
            └─ Lambda (dotnet8, SnapStart, alias=live)
                 └─ 既存の ASP.NET Core (wwwroot も同梱)
                      └─ stdout → CloudWatch Logs (保持 14 日)
```

★ `query_string_behavior = "all"` は落とせない。`BadgeController` が `Request.QueryString` を
そのまま shields.io に転送しているため (`?color=blue` などのカスタマイズ)、キャッシュキーから
クエリ文字列を外すと全バッジが最初にキャッシュされた 1 枚に化ける。

## 責務の分担

- **Terraform** — 関数という「器」、エイリアス、Function URL、CloudFront、証明書、DNS
- **GitHub Actions** (`.github/workflows/deploy-lambda.yml`) — 関数の「中身」(コード)

`aws_lambda_function` は `filename` / `source_code_hash` を `ignore_changes` しているので、
CI が入れたコードを Terraform が plan のたびにプレースホルダーへ巻き戻すことはない。
`aws_lambda_alias` の `function_version` も同じ理由で `ignore_changes` している。

## 日常の操作

移行は完了済み。**変数の既定値が稼働中の状態そのもの**なので、`terraform.tfvars` が無くても
素の `terraform apply` で現状と一致する (差分ゼロ)。

```bash
cd terraform
terraform init -backend-config=backend.hcl
terraform plan     # No changes になるのが正常
```

> ⚠️ `enable_custom_domain` / `manage_dns` を false に倒すと、**証明書とホストゾーンが消えて
> バッジ配信もメールも止まる**。段階移行のために用意したフラグであって、通常運用で触るものではない。
> plan に `aws_route53_zone.main[0] will be destroyed` が出たら、その apply は実行しないこと。

## 新しい環境をゼロから構築する場合

以下は**別アカウント等に一から作り直すとき**の手順。稼働中の環境には不要。

**順序が重要。** Terraform が最初に作る関数の中身はプレースホルダー ZIP なので、
CI を一度走らせるまでバッジは配信できない。動作確認より前に手順 3 を必ず終わらせること。

### 1. 土台を作る

```bash
cd terraform
cp terraform.tfvars.example terraform.tfvars   # 編集する
terraform init -backend-config=backend.hcl
terraform apply -var='enable_snapstart=false' -var='enable_custom_domain=false' -var='manage_dns=false'
```

SnapStart はここでは切っておく。SnapStart はバージョン発行時に Init を走らせて
スナップショットを取るため、実コードが載る前のプレースホルダー ZIP では発行に失敗する。

### 2. GitHub 側の設定

`terraform output` の値を設定する。`CLOUDFRONT_DISTRIBUTION_ID` が未設定だと
`deploy-lambda` はキャッシュ無効化ステップで意図的に失敗する。

| 設定先 | キー | 値 |
| --- | --- | --- |
| Variables | `CLOUDFRONT_DISTRIBUTION_ID` | `cloudfront_distribution_id` |
| Variables | `AWS_DEPLOY_ROLE_ARN` | `github_actions_role_arn` |

`deploy-lambda.yml` は**長期のアクセスキーを使わない**。実行ごとに発行される OIDC トークンで
`github-oidc.tf` が作るロールを引き受ける。Secrets は不要。

ロールの信頼ポリシーは `repo:<owner>/<repo>:ref:refs/heads/master` に固定してある
(`var.github_deploy_refs`)。別ブランチから手動実行したくなったらこの変数に足す。
ここを緩めると同じ GitHub OIDC を使う任意のリポジトリから引き受けられてしまうので、
`repo:*` のような書き方はしないこと。

### 3. 実コードを載せる

`master` に push、または `deploy-lambda` を workflow_dispatch で実行する。
これで `live` エイリアスがプレースホルダーから実コードのバージョンに移る。

### 4. SnapStart を有効化する

```bash
terraform apply -var='enable_custom_domain=false' -var='manage_dns=false'
```

`enable_snapstart` の既定は true なので、`-var` を外すだけで有効になる。
設定が効くのは**次に発行されるバージョンから**なので、有効化したあともう一度
`deploy-lambda` を走らせる。

## 段階移行の手順

対象は **apex ドメイン** `vsmarketplacebadges.dev`。DNS は元々 **Gandi LiveDNS** にあり、
apex は Gandi の ALIAS で App Runner を指していた。これを Route 53 に移管する。

apex は CNAME が使えず、CloudFront には固定 IP が無いので ALIAS が必須。
**実際の切り替えは Terraform ではなく Gandi のレジストラ設定で NS を変える操作**になる。

### 切り戻しは DNS ではなくオリジン切替で行う

apex を App Runner に戻す DNS 手段が存在しない。Route 53 で App Runner を ALIAS ターゲットに
できるのは **2022-08-01 以降に作成されたサービスだけ**で、このサービスは 2022-05-06 作成のため
対象外。そこで DNS は常に CloudFront を指したままにし、CloudFront のオリジンを差し替える。

```
apex A/AAAA ALIAS → CloudFront  (固定・変更しない)
                      ├─ origin = Lambda Function URL    ← 通常
                      └─ origin = App Runner 既定ドメイン ← 切り戻し
```

```bash
terraform apply -var='rollback_to_apprunner=true'    # 切り戻し
terraform apply                                       # 復帰
```

DNS の TTL に左右されず、反映は CloudFront の伝播 (数分) だけで済む。
ただし切り戻し先の App Runner は **2022 年のコードを配信する** (CLAUDE.md 参照)。

### フェーズ 1 — 裏で検証

「初回構築」の手順 4 まで終わっていること。CloudFront の既定ドメインで動作確認する:

```bash
DOMAIN=$(terraform output -raw cloudfront_domain_name)
curl -s "https://$DOMAIN/version-short/ms-dotnettools.csharp.svg" | head -c 200
curl -s "https://$DOMAIN/downloads/ms-dotnettools.csharp.svg?color=blue" | head -c 200
curl -sI "https://$DOMAIN/"
```

確認したいこと:

- クエリ文字列ごとに別のバッジが返る
- 2 回目以降のレスポンスヘッダーに `X-Cache: Hit from cloudfront` が出る
- Function URL を直接叩くと 403 になる (`terraform output -raw lambda_function_url`)
- CloudWatch Logs にログが出ている

この時点で本番トラフィックは App Runner のまま。

### フェーズ 2 — 証明書を発行して Gandi で検証する

**NS 移管より先に証明書を通しておく。** ACM の DNS 検証は権威 DNS を見るため、NS が Gandi の
うちは Route 53 に検証レコードを入れても検証されない。順序を逆にすると、NS を切り替えた瞬間に
証明書エラーになる。

```bash
terraform apply -var='enable_custom_domain=true'
terraform output acm_validation_records
```

出力された CNAME を **Gandi の DNS に手動で追加**する。発行されると `terraform apply` が完了する
(`aws_acm_certificate_validation` が発行待ちをする)。

DNS を変えずに独自ドメイン名で CloudFront に届くか確認できる:

```bash
DOMAIN=$(terraform output -raw cloudfront_domain_name)
curl -s --resolve "vsmarketplacebadges.dev:443:$(dig +short $DOMAIN | head -1)" \
  "https://vsmarketplacebadges.dev/version-short/ms-dotnettools.csharp.svg" | head -c 200
```

### フェーズ 3 — Route 53 にゾーンを作って突き合わせる

```bash
terraform apply -var='enable_custom_domain=true' -var='manage_dns=true'
```

NS はまだ Gandi なので**無影響**。作られたゾーンの中身を Gandi のゾーンファイルと突き合わせる。

```bash
ZONE=$(terraform output -raw route53_zone_id 2>/dev/null || \
  aws route53 list-hosted-zones-by-name --dns-name vsmarketplacebadges.dev \
    --query 'HostedZones[0].Id' --output text)
aws route53 list-resource-record-sets --hosted-zone-id "$ZONE" \
  --query 'ResourceRecordSets[].[Name,Type,ResourceRecords[].Value,AliasTarget.DNSName]' --output text
```

**MX と SPF を落とすとメール受信が止まる。** apex の ALIAS 以外はすべて Gandi と同じ内容に
なっていること。移管対象は以下:

| 名前 | 種別 | 用途 |
| --- | --- | --- |
| apex | A/AAAA ALIAS → CloudFront | バッジ配信 (Gandi では App Runner を指していた) |
| apex | MX | Gandi メール。落とすと受信が止まる |
| apex | TXT | SPF と Google Search Console |
| `www` | CNAME | Gandi ウェブリダイレクト |
| `blog` | CNAME | Gandi ブログ |
| `webmail` | CNAME | Gandi ウェブメール |
| `_85731b97…` / `_7893e7c8…` | CNAME | App Runner 証明書の更新用。撤去まで必要 |
| ACM 検証用 | CNAME | CloudFront 証明書の更新用 |

> Gandi のゾーンには同じ検証レコードが「相対名」と「FQDN を相対名の欄に入れてしまったもの」の
> 2 通りで登録されている (`….dev.vsmarketplacebadges.dev` という二重ドメインになっている)。
> 無害だが不要なので移管していない。

### フェーズ 4 — NS を切り替える (実際の切り替え)

```bash
terraform output route53_name_servers
```

この 4 本を **Gandi のレジストラ設定 (ネームサーバー)** に登録する。ここが切り替え点。

浸透には時間がかかる。Gandi の NS の TTL に加え、レジストリ側の反映待ちがあるため、
**切り替え当日は数時間の並行状態**になると見ておくこと。並行中はどちらの権威に当たっても
バッジは返る (Gandi → App Runner / Route 53 → CloudFront) ので配信は途切れない。

浸透の確認:

```bash
dig +short NS vsmarketplacebadges.dev
dig +short vsmarketplacebadges.dev
curl -sI https://vsmarketplacebadges.dev/version-short/ms-dotnettools.csharp.svg | grep -i x-cache
```

`x-cache` が出れば CloudFront 経由に切り替わっている。

問題が出たら DNS ではなく**オリジン切替**で戻す (上記)。

### 移行完了後の後片付け

NS 切り替えが定着したら:

1. App Runner サービスを削除。**削除すると `rollback_to_apprunner` が使えなくなる**ので、
   しばらく様子を見てから
2. `.github/workflows/push-ecr.yml` と `Dockerfile` を削除 (ローカル開発は `dotnet watch run`)
3. ECR リポジトリを削除。これで長期アクセスキーの利用者がいなくなるので、IAM ユーザー
   `for-github-actions` とそのアクセスキー、Secrets の `AWS_ACCESS_KEY_ID` /
   `AWS_SECRET_ACCESS_KEY` / `AWS_ECR_REPO_NAME` も削除する
4. `apprunner_validation_records` と `apprunner_service_url` を削除
5. S3 の `vsmarketplace-badges/logs` を必要に応じて削除 (ログ出力先は CloudWatch に移行済み)

## タイムアウトの予算

3 か所が連動している。1 つだけ変えないこと。

| 層 | 値 | 場所 |
| --- | --- | --- |
| 外部 HTTP 1 呼び出し (Polly ハードキャップ) | 10 秒 | `Startup.cs` の `TotalTimeoutPolicy` |
| 1 リクエスト最悪 | 30 秒 | Marketplace 10×2 + shields.io 10 |
| Lambda | 35 秒 | `var.lambda_timeout` |
| CloudFront オリジン応答 | 40 秒 | `var.origin_read_timeout` |

内訳は `Startup.cs` のコメントを参照。`VSMarketplaceService.LoadVsmItemDataFromApi` が失敗時に
`CoreRequest` をもう一度呼ぶため、Marketplace 側は他方の 2 倍かかる点に注意。

## コストの見どころ

- **Lambda** — 呼び出し回数と実行時間のみ。バッジは 1 時間 CloudFront にキャッシュされ、
  GitHub の README に貼られた分は camo プロキシも挟まるので、オリジンへの到達はごく少ない。
- **CloudWatch Logs** — 保存量で課金される。`log_retention_days` を必ず有限に保つこと。
  ローカル実行で確認した限り `UseCacheService` の `"Redis error."` は 1 件も出ていない
  (`IDistributedCache` はフレームワーク既定のプロセス内実装が解決されている)。ただし
  そのキャッシュは実行環境ごとに独立するため、Lambda ではコールドスタートのたびに空になる。
  実際に効くキャッシュは CloudFront 側だと考えてよい。
- **CloudFront** — 転送量課金。無効化は月 1000 パスまで無料。
