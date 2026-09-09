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
    SnapStart を有効にするか。.NET のコールドスタートを縮める。

    既定は稼働中の状態に合わせた true。**新しい環境をゼロから構築するときの初回 apply だけ**
    false にすること。SnapStart はバージョン発行時に Init を実行してスナップショットを取るため、
    まだ実コードが載っていないプレースホルダー ZIP のままだと発行に失敗する。
    手順は README.md「初回構築」を参照。
  EOT
  type        = bool
  default     = true
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

variable "rollback_to_apprunner" {
  description = <<-EOT
    CloudFront のオリジンを Lambda Function URL から App Runner に切り替える。これが切り戻しスイッチ。

    DNS ではなくオリジンを切り替えるのは、apex を App Runner に戻す DNS 手段が無いため。
    Route 53 で App Runner を ALIAS ターゲットにできるのは 2022-08-01 以降に作成された
    サービスだけで、このサービスは 2022-05-06 作成で対象外。
    オリジン切替なら DNS に触れず、反映も CloudFront の伝播 (数分) で済む。

    なお切り戻し先の App Runner は 2022 年のコードを配信する (CLAUDE.md 参照)。
  EOT
  type        = bool
  default     = false
}

variable "domain_name" {
  description = "バッジを配信する独自ドメイン。apex ドメインを想定している。"
  type        = string
  default     = "vsmarketplacebadges.dev"
}

variable "apprunner_service_url" {
  description = <<-EOT
    App Runner サービスの既定ドメイン。rollback_to_apprunner = true のとき
    CloudFront のオリジンになる。
  EOT
  type        = string
  default     = "x2k3hfprwf.ap-northeast-1.awsapprunner.com"
}

variable "apprunner_validation_records" {
  description = <<-EOT
    App Runner のカスタムドメイン証明書の検証用 CNAME (相対名 => 値)。
    Gandi から引き継ぐ。App Runner を撤去するまで証明書の自動更新に必要なので落とさないこと。
  EOT
  type        = map(string)
  default = {
    "_85731b979fc3be6f41e8139bab076b73"                                 = "_d0401947bf62b9cb7df39c4cd0ee02b1.mybbdzzyvz.acm-validations.aws."
    "_7893e7c832547de072c77b6dde1ae737.2a57j77tquppxkxpp0ow3wj78ekl878" = "_ea93d36bd33fd80aa6334ef02015105c.zfmzgmvxlk.acm-validations.aws."
  }
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
