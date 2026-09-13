# インフラ

バッジ配信基盤の Terraform 定義。

## 構成

```
vsmarketplacebadges.dev (Route 53 / apex)
  └─ CloudFront                         ACM 証明書は us-east-1
       ├─ Cache Policy: 許可したクエリ文字列だけをキャッシュキーに含める ★
       ├─ TTL 3600 (オリジンの [ResponseCache(Duration = 3600)] に合わせる)
       ├─ Security Headers: HSTS / nosniff / X-Frame-Options / Referrer-Policy / CORP
       ├─ Access Logs → S3 (var.access_log_retention_days 日)
       └─ Origin: Lambda Function URL (AWS_IAM + OAC で直叩きを封鎖)
            └─ Lambda (dotnet10, alias=live)  ※SnapStart はコスト都合で無効
                 └─ ASP.NET Core (wwwroot も同梱)
                      └─ stdout → CloudWatch Logs (保持 14 日)
```

★ クエリ文字列は **allowlist** (`var.forwarded_query_strings`) で扱う。両側に落とし穴がある。

- **キャッシュキーから完全に外してはいけない。** `BadgeController` が `Request.QueryString` を
  そのまま shields.io に転送しているため (`?color=blue` などのカスタマイズ)、外すと全バッジが
  最初にキャッシュされた 1 枚に化ける。
- **かといって `all` にもしない。** `?cb=<乱数>` を付けるだけでキャッシュを迂回でき、Lambda と
  上流 (Marketplace / shields.io) へ無制限にリクエストを誘発できる。課金だけでなく、上流から
  当サービスの IP が遮断されるリスクがある。

shields.io が新しいパラメータを増やしたら `forwarded_query_strings` に足す。足し忘れても
そのパラメータが効かなくなるだけで壊れはしない。**CloudFront の上限は既定 10 個**で、
超えると `TooManyQueryStringsInCachePolicy` で apply が失敗する。

## 責務の分担

- **Terraform** — 関数という「器」、エイリアス、Function URL、CloudFront、証明書、DNS、監視
- **GitHub Actions** (`.github/workflows/deploy-lambda.yml`) — 関数の「中身」(コード)

`aws_lambda_function` は `filename` / `source_code_hash` を `ignore_changes` しているので、
CI が入れたコードを Terraform が plan のたびにプレースホルダーへ巻き戻すことはない。
`aws_lambda_alias` の `function_version` も同じ理由で `ignore_changes` している。

## 日常の操作

**変数の既定値が稼働中の状態そのもの**なので、`terraform.tfvars` が無くても素の
`terraform apply` で現状と一致する (差分ゼロ)。

```bash
cd terraform
terraform init -backend-config=backend.hcl
terraform plan     # No changes になるのが正常
```

> ⚠️ `enable_custom_domain` / `manage_dns` を false に倒すと、**証明書とホストゾーンが消えて
> バッジ配信もメールも止まる**。環境をゼロから作るときのために残してあるフラグであって、
> 通常運用で触るものではない。plan に `aws_route53_zone.main[0] will be destroyed` が出たら、
> その apply は実行しないこと。

## 切り戻し

**Lambda エイリアス (`live`) の版戻し**で行う。

```bash
aws lambda update-alias --function-name vsmarketplace-badges --name live \
  --function-version <前の版>
```

即時に反映される。`deploy-lambda.yml` が直近 3 世代を残すので 1〜2 世代前まで戻せる。

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

## 監視

CloudWatch アラーム 4 種と AWS Budgets を `monitoring.tf` で定義している。通知先は
`var.alarm_email` (gitignore 済みの `terraform.tfvars` に置く)。

> ⚠️ **SNS のメール購読は未確認のまま 3 日で削除される。** apply で購読が作り直されたら
> 確認メールのリンクを必ず踏むこと。踏まないとアラームが発報しても誰にも届かない。

CloudWatch アラームは同一リージョンの SNS トピックしか叩けず、CloudFront のメトリクスは
us-east-1 にしか出ないため、トピックを 2 リージョンに持っている。

## コストの見どころ

- **Lambda** — 呼び出し回数と実行時間のみ。バッジは 1 時間 CloudFront にキャッシュされ、
  GitHub の README に貼られた分は camo プロキシも挟まるので、オリジンへの到達はごく少ない。
- **SnapStart** — 有効にすると**発行済みバージョンごとに**スナップショット保管料がかかる
  (1024MB で月 $3.90/本)。効果が小さく費用が勝つため無効にしている。判断の根拠は
  `variables.tf` の `enable_snapstart` と CLAUDE.md。
- **CloudWatch Logs** — 保存量で課金される。`log_retention_days` を必ず有限に保つこと。
- **CloudFront** — 転送量課金。無効化は月 1000 パスまで無料。

プロセス内キャッシュ (`UseCacheService`) は実行環境ごとに独立し、コールドスタートのたびに
空になる。実際に効くキャッシュは CloudFront 側だと考えてよい。

## 新しい環境をゼロから構築する場合

別アカウント等に一から作り直すときの手順。稼働中の環境には不要。

**順序が重要。** Terraform が最初に作る関数の中身はプレースホルダー ZIP なので、
CI を一度走らせるまでバッジは配信できない。

### 1. 土台を作る

```bash
cp terraform.tfvars.example terraform.tfvars   # 編集する
terraform init -backend-config=backend.hcl
terraform apply -var='enable_custom_domain=false' -var='manage_dns=false'
```

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
(`var.github_deploy_refs`)。ここを緩めると同じ GitHub OIDC を使う任意のリポジトリから
引き受けられてしまうので、`repo:*` のような書き方はしないこと。

### 3. 実コードを載せる

`master` に push、または `deploy-lambda` を workflow_dispatch で実行する。
これで `live` エイリアスがプレースホルダーから実コードのバージョンに移る。

CloudFront の既定ドメインで動作確認する。

```bash
DOMAIN=$(terraform output -raw cloudfront_domain_name)
curl -s "https://$DOMAIN/version-short/ms-dotnettools.csharp.svg" | head -c 200
```

- クエリ文字列ごとに別のバッジが返るか
- 2 回目以降に `X-Cache: Hit from cloudfront` が出るか
- Function URL を直接叩くと 403 になるか (`terraform output -raw lambda_function_url`)

### 4. 独自ドメインを付ける

```bash
terraform apply -var='enable_custom_domain=true' -var='manage_dns=true'
```

## 付録: DNS を別のレジストラから移管する場合

apex ドメインは CNAME が使えず、CloudFront に固定 IP も無いため ALIAS が必須。
Route 53 以外の DNS から移す場合は順序に注意する。

1. **証明書を先に通す。** ACM の DNS 検証は権威 DNS を見るため、NS を移す前に検証用 CNAME を
   **移管元に手動で追加**する (`terraform output acm_validation_records`)。順序を逆にすると
   NS を切り替えた瞬間に証明書エラーになる。
2. **Route 53 にゾーンを作り、移管元と突き合わせる。** NS を変えるまでは無影響。
   **MX と SPF を落とすとメール受信が止まる。** 移管元のゾーンファイルをエクスポートして
   1 件ずつ確認すること。外部からは AXFR できないので、公開解決の総当たりでは漏れる。
3. **レジストラの NS を Route 53 に向ける。** ここが実際の切り替え点。浸透には数時間かかり、
   その間はリゾルバごとに新旧が混在する。

```bash
terraform output route53_name_servers
dig +short NS vsmarketplacebadges.dev    # 浸透の確認
```
