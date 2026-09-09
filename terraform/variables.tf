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
    SnapStart を有効にするか。.NET のコールドスタートを大幅に縮める。

    既定を false にしてあるのは、SnapStart がバージョン発行時に Init を実行して
    スナップショットを取るためで、まだ実コードが載っていないプレースホルダー ZIP のまま
    true にすると初回 apply がバージョン発行で失敗する。実コードを CI でデプロイしたあと
    true に上げる。手順は README.md を参照。
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
    段階移行フェーズ 2。ACM 証明書を発行し、CloudFront に独自ドメインを別名として付ける。
    DNS はまだ App Runner を向いたままなので、実トラフィックには影響しない。
  EOT
  type        = bool
  default     = false
}

variable "enable_dns_cutover" {
  description = <<-EOT
    段階移行フェーズ 3。Route 53 のレコードを App Runner から CloudFront へ切り替える。
    これが実際の切り替えスイッチで、false に戻せばロールバックになる。
    enable_custom_domain = true が前提。
  EOT
  type        = bool
  default     = false
}

variable "domain_name" {
  description = "バッジを配信する独自ドメイン (例: badges.example.com)。enable_custom_domain = true のとき必須。"
  type        = string
  default     = ""
}

variable "route53_zone_id" {
  description = "domain_name を含む Route 53 ホストゾーン ID。enable_custom_domain = true のとき必須。"
  type        = string
  default     = ""
}

variable "apprunner_service_url" {
  description = <<-EOT
    ロールバック先として残す App Runner サービスの既定ドメイン
    (例: xxxxxxxx.ap-northeast-1.awsapprunner.com)。
    enable_dns_cutover = false のあいだ、Route 53 レコードはこちらを指し続ける。
  EOT
  type        = string
  default     = ""
}

variable "tags" {
  description = "全リソースに付与するタグ。"
  type        = map(string)
  default = {
    Project   = "VSMarketplaceBadges"
    ManagedBy = "terraform"
  }
}
