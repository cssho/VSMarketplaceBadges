variable "region" {
  description = "Lambda と CloudWatch Logs を置くリージョン。App Runner と揃えて ap-northeast-1。"
  type        = string
  default     = "ap-northeast-1"
}

variable "function_name" {
  description = "Lambda 関数名。ロググループ名や各リソースの名前の接頭辞にもなる。"
  type        = string
  default     = "vsmarketplace-badges"
}

variable "lambda_memory_size" {
  description = <<-EOT
    Lambda のメモリ (MB)。Lambda は割り当てメモリに比例して CPU も増えるため、
    ASP.NET Core では 512 より 1024 のほうが実行時間が縮み、結果として安くなることが多い。
    請求実績を見て調整する。
  EOT
  type        = number
  default     = 1024
}

variable "lambda_timeout" {
  description = <<-EOT
    Lambda のタイムアウト (秒)。Startup.cs の Polly 予算 (1 リクエスト最悪 30 秒) の外側、
    かつ CloudFront のオリジン応答タイムアウト (var.origin_read_timeout) の内側に置く。
    3 つは連動しているので単独で変えないこと。
  EOT
  type        = number
  default     = 35
}

variable "origin_read_timeout" {
  description = "CloudFront がオリジン (Lambda) の応答を待つ秒数。lambda_timeout より長くする。"
  type        = number
  default     = 40
}

variable "log_retention_days" {
  description = <<-EOT
    CloudWatch Logs の保持日数。App Runner 時代の S3 への無期限蓄積と違い、
    CloudWatch は保存量で課金されるため必ず有限にする。
  EOT
  type        = number
  default     = 14
}

variable "enable_snapstart" {
  description = <<-EOT
    SnapStart を有効にするか。**このワークロードでは割に合わないので既定 false。**

    一度有効にして実測したが、費用対効果が成立しなかった:

      効果: コールドスタート 2405ms → 1923ms (-482ms)
      費用: スナップショット 1 本 (1024MB) あたり月 $3.90
            単価 $1.5046e-6 / GB-秒 を実請求から逆算した値

    SnapStart は**発行済みバージョンごとに**スナップショットを保管し、その保管料が課金される。
    デプロイのたびにバージョンが増えるため、放置すると積み上がる。実際 5 バージョンが
    キャッシュされ、Lambda 費用の 94% (月換算 $19.50 相当) を占めていた。旧 App Runner の
    月 $11.99 を上回り、コスト削減という移行の目的を覆す水準だった。

    実行分 (GB-秒・リクエスト) は無料枠に収まり $0 なので、SnapStart を切れば Lambda 費用は
    ほぼゼロになる。バッジは CloudFront に 1 時間キャッシュされ、コールドスタートに当たる
    利用者は稀。482ms のために月 $4〜20 は見合わない。

    再度有効にするなら、バージョンの掃除 (deploy-lambda.yml が実施) が効いていることと、
    メモリを下げてスナップショットを小さくすることを併せて検討すること。
  EOT
  type        = bool
  default     = false
}

variable "price_class" {
  description = <<-EOT
    CloudFront のエッジロケーション範囲。PriceClass_100 は北米・欧州のみで
    日本のエッジを含まないため既定は PriceClass_200 (北米・欧州・アジア)。
  EOT
  type        = string
  default     = "PriceClass_200"
}

variable "enable_custom_domain" {
  description = <<-EOT
    ACM 証明書を発行し、CloudFront に独自ドメインを別名として付ける。
    DNS はまだ Gandi が権威なので、これだけでは実トラフィックに影響しない。

    ACM の DNS 検証は権威 DNS を見るため、NS を移管するより先にこれを通しておく必要がある。
    検証用 CNAME は Gandi に手動で追加する (README.md の手順)。

    移管は完了済みなので既定は true。**false に倒すと本番の証明書が消えて配信が止まる。**
  EOT
  type        = bool
  default     = true
}

variable "manage_dns" {
  description = <<-EOT
    Route 53 にホストゾーンとレコード一式を作る。

    移管は完了済みで、このゾーンが vsmarketplacebadges.dev の権威。既定は true。
    **false に倒すとホストゾーンごと消え、バッジ配信もメールも止まる。**
    段階移行のために false から始めた経緯は README.md を参照。
  EOT
  type        = bool
  default     = true
}

variable "domain_name" {
  description = "バッジを配信する独自ドメイン。apex ドメインを想定している。"
  type        = string
  default     = "vsmarketplacebadges.dev"
}

variable "forwarded_query_strings" {
  description = <<-EOT
    CloudFront がキャッシュキーに含め、オリジンへ転送するクエリ文字列。

    ここに無いパラメータは**キャッシュキーにも入らず、オリジンにも渡らない**。
    `?cb=<乱数>` のような細工でキャッシュを迂回し、Lambda と上流 (Marketplace / shields.io) へ
    無制限にリクエストを誘発する攻撃を防ぐのが目的。全転送 (all) だと実際に迂回できていた。

    中身は shields.io が解釈するパラメータと、このアプリ自身が使う subject / color。
    shields.io が新しいパラメータを増やしたらここに足す。足し忘れるとそのパラメータが
    効かなくなるだけで、壊れはしない。

    **CloudFront のキャッシュポリシーに入れられるクエリ文字列は既定で 10 個まで。**
    超えると TooManyQueryStringsInCachePolicy で apply が失敗する。増やしたい場合は
    どれかを落とすか、サービスクォータの引き上げを申請する。

    shields.io の cacheSeconds は意図的に含めていない。当サービスのキャッシュ時間は
    CloudFront 側の設定で決まるため呼び出し側が指定しても実効性が薄く、10 個の枠を使うに
    値しないと判断した。
  EOT
  type        = list(string)
  default = [
    "color",
    "label",
    "labelColor",
    "link",
    "logo",
    "logoColor",
    "logoSize",
    "logoWidth",
    "style",
    "subject",
  ]
}

variable "access_log_retention_days" {
  description = "CloudFront アクセスログの保持日数。攻撃の事後追跡用なので長期保存は不要。"
  type        = number
  default     = 90
}

variable "alarm_email" {
  description = <<-EOT
    アラームと予算超過の通知先メールアドレス。空ならメール購読と予算を作らない
    (アラーム自体は SNS トピックに飛ぶので、あとから購読を足せる)。

    購読は確認メールのリンクを踏むまで Pending のままなので、apply 後に承認すること。
  EOT
  type        = string
  default     = ""
}

variable "monthly_budget_usd" {
  description = <<-EOT
    月額予算 (USD)。超過の実績 80% と、月末予測 100% で通知する。
    App Runner からの移行はコスト削減が目的だったので、想定を超えたら気づけるようにしておく。
  EOT
  type        = string
  default     = "5"
}

variable "github_repository" {
  description = "OIDC でデプロイロールを引き受けさせる GitHub リポジトリ (owner/repo)。"
  type        = string
  default     = "cssho/VSMarketplaceBadges"
}

variable "github_deploy_refs" {
  description = <<-EOT
    デプロイロールの引き受けを許可する ref。信頼ポリシーの sub をここまで絞る。
    deploy-lambda.yml は master への push と workflow_dispatch で動くが、
    workflow_dispatch は既定ブランチ上のワークフローしか一覧に出ないため master だけで足りる。
    別ブランチから手動実行したくなったらここに足す。
  EOT
  type        = list(string)
  default     = ["refs/heads/master"]
}

variable "tags" {
  description = "全リソースに付与するタグ。"
  type        = map(string)
  default = {
    Project   = "VSMarketplaceBadges"
    ManagedBy = "terraform"
  }
}
