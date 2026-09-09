# インフラ (App Runner → Lambda + CloudFront)

App Runner がメンテナンスモードに入った (2026/4/30 以降は新規顧客の受付停止、新機能追加なし)
ことと、常時起動コンテナの月額を落とすことを目的に、配信基盤を Lambda + CloudFront へ移す。

## 構成

```
Route 53 (独自ドメイン)
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

## 初回構築

**順序が重要。** Terraform が最初に作る関数の中身はプレースホルダー ZIP なので、
CI を一度走らせるまでバッジは配信できない。フェーズ 1 の動作確認より前に手順 3 を必ず終わらせること。

### 1. 土台を作る

```bash
cd terraform
cp terraform.tfvars.example terraform.tfvars   # 編集する
terraform init -backend-config=backend.hcl
terraform apply                                # enable_snapstart は既定 false
```

SnapStart はここではまだ切ったままにする。SnapStart はバージョン発行時に Init を走らせて
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
terraform apply -var='enable_snapstart=true'
```

設定が効くのは**次に発行されるバージョンから**なので、有効化したあともう一度
`deploy-lambda` を走らせる。以降は `terraform.tfvars` に `enable_snapstart = true` を書いておく。

## 段階移行の手順

`enable_custom_domain` と `enable_dns_cutover` の 2 つのフラグで進める。
App Runner は削除せず動かしたまま進め、フェーズ 3 を切り戻せる状態を保つ。

### フェーズ 1 — 裏で検証

「初回構築」の手順 4 まで終わっていること (実コードが `live` エイリアスに載っていないと
以下の確認はすべて失敗する)。両フラグとも false のままなので本番トラフィックは App Runner。
CloudFront の既定ドメインで動作確認する:

```bash
DOMAIN=$(terraform output -raw cloudfront_domain_name)
curl -s "https://$DOMAIN/version-short/ms-dotnettools.csharp.svg" | head -c 200
curl -s "https://$DOMAIN/downloads/ms-dotnettools.csharp.svg?color=blue" | head -c 200
curl -sI "https://$DOMAIN/"                      # index.html が返ること
```

確認したいこと:

- クエリ文字列ごとに別のバッジが返る (`?color=blue` を付けたものと付けないもので中身が違う)
- 2 回目以降のレスポンスヘッダーに `X-Cache: Hit from cloudfront` が出る
- Function URL を直接叩くと 403 になる (`terraform output -raw lambda_function_url`)
- CloudWatch Logs にログが出ている

### フェーズ 2 — 証明書を付ける

```bash
terraform apply -var='enable_custom_domain=true'
```

ACM 証明書が発行され、CloudFront に独自ドメインが別名として付く。**DNS はまだ App Runner を
向いたままなので本番トラフィックには影響しない。** 独自ドメイン名で CloudFront に届くかは
DNS を変えずに確認できる:

```bash
DOMAIN=$(terraform output -raw cloudfront_domain_name)
curl -s --resolve "badges.example.com:443:$(dig +short $DOMAIN | head -1)" \
  "https://badges.example.com/version-short/ms-dotnettools.csharp.svg" | head -c 200
```

### フェーズ 3 — DNS を切り替える

```bash
terraform apply -var='enable_custom_domain=true' -var='enable_dns_cutover=true'
```

Route 53 のレコードが CloudFront への ALIAS になる。TTL は 60 秒にしてあるので反映は速い。

**ロールバック** — App Runner はまだ動いているので、`enable_dns_cutover=false` で apply し直せば
戻る (`apprunner_service_url` を設定してあれば App Runner 向き CNAME が復元される)。

### 移行完了後の後片付け

DNS 切り替えが定着したら:

1. App Runner サービスを削除
2. `.github/workflows/push-ecr.yml` と `Dockerfile` を削除 (ローカル開発は `dotnet watch run`)
3. ECR リポジトリを削除。これで長期アクセスキーの利用者がいなくなるので、IAM ユーザー
   `for-github-actions` とそのアクセスキー、Secrets の `AWS_ACCESS_KEY_ID` /
   `AWS_SECRET_ACCESS_KEY` / `AWS_ECR_REPO_NAME` も削除する
4. S3 の `vsmarketplace-badges/logs` を必要に応じて削除 (ログ出力先は CloudWatch に移行済み)

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
