# ----------------------------------------------------------------------------
# 証明書と DNS
#
# 対象は apex ドメイン (vsmarketplacebadges.dev)。サブドメインではないため CNAME が使えず、
# CloudFront を向けるには ALIAS が要る。DNS は元々 Gandi LiveDNS にあり、apex は Gandi の
# ALIAS で App Runner を指していた。これを Route 53 に移管する。
#
# 移管の順序が重要:
#   1. 証明書を発行する (このファイルの aws_acm_certificate)
#   2. 検証用 CNAME 2 本を **Gandi に手動で追加** する。ACM の DNS 検証は権威 DNS を見るので、
#      NS が Gandi のうちは Route 53 に入れても検証されない
#   3. CloudFront に証明書と別名を付ける (enable_custom_domain = true)
#   4. Route 53 にゾーンとレコード一式を作る (manage_dns = true)。NS はまだ Gandi なので無影響
#   5. Gandi のレジストラ設定で NS を Route 53 に向ける ← ここが実際の切り替え
#
# 切り戻しは DNS ではなく CloudFront のオリジン切替で行う (var.rollback_to_apprunner)。
# App Runner を Route 53 の ALIAS ターゲットにできるのは 2022-08-01 以降に作成された
# サービスだけで、このサービスは 2022-05-06 作成のため対象外。DNS 経路での切り戻しは
# そもそも成立しない。詳細は README.md を参照。
# ----------------------------------------------------------------------------

# ----------------------------------------------------------------------------
# ACM 証明書 (us-east-1)
# ----------------------------------------------------------------------------
resource "aws_acm_certificate" "this" {
  count    = var.enable_custom_domain ? 1 : 0
  provider = aws.us_east_1

  domain_name       = var.domain_name
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }
}

# 検証は権威 DNS 上のレコードを見て行われる。NS 移管前は Gandi に手で入れる必要があるため、
# ここでは Route 53 のレコードではなく証明書自身が要求する FQDN を渡して発行完了を待つだけに
# している。こうしておくと移管の前後どちらでも同じコードで通る。
resource "aws_acm_certificate_validation" "this" {
  count    = var.enable_custom_domain ? 1 : 0
  provider = aws.us_east_1

  certificate_arn         = aws_acm_certificate.this[0].arn
  validation_record_fqdns = [for o in aws_acm_certificate.this[0].domain_validation_options : o.resource_record_name]
}

# ----------------------------------------------------------------------------
# Route 53 ホストゾーン
#
# manage_dns = true にしてもレジストラの NS を変えるまでは誰も参照しない。
# 先に作って中身を突き合わせ、確認できてから NS を切り替えるための段取り。
# ----------------------------------------------------------------------------
resource "aws_route53_zone" "main" {
  count = var.manage_dns ? 1 : 0

  name    = var.domain_name
  comment = "Migrated from Gandi LiveDNS"
}

locals {
  zone_id = var.manage_dns ? aws_route53_zone.main[0].zone_id : null
}

# --- バッジ配信 -------------------------------------------------------------
# apex から CloudFront への ALIAS。CloudFront に固定 IP は無いので ALIAS 以外に手段がない。
resource "aws_route53_record" "apex" {
  count = var.manage_dns ? 1 : 0

  zone_id = local.zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.badges.domain_name
    zone_id                = aws_cloudfront_distribution.badges.hosted_zone_id
    evaluate_target_health = false
  }
}

resource "aws_route53_record" "apex_ipv6" {
  count = var.manage_dns ? 1 : 0

  zone_id = local.zone_id
  name    = var.domain_name
  type    = "AAAA"

  alias {
    name                   = aws_cloudfront_distribution.badges.domain_name
    zone_id                = aws_cloudfront_distribution.badges.hosted_zone_id
    evaluate_target_health = false
  }
}

# --- Gandi から引き継ぐレコード ---------------------------------------------
# 落とすとメール受信や既存サブドメインが止まる。NS を切り替える前に、Gandi のゾーンファイルと
# 突き合わせて過不足が無いことを必ず確認すること (README.md の手順)。

# Gandi のメールサービス。これを落とすと受信が止まる。
resource "aws_route53_record" "mx" {
  count = var.manage_dns ? 1 : 0

  zone_id = local.zone_id
  name    = var.domain_name
  type    = "MX"
  ttl     = 10800
  records = [
    "10 spool.mail.gandi.net.",
    "50 fb.mail.gandi.net.",
  ]
}

# SPF (Gandi メール) と Google Search Console の所有権確認。
resource "aws_route53_record" "txt" {
  count = var.manage_dns ? 1 : 0

  zone_id = local.zone_id
  name    = var.domain_name
  type    = "TXT"
  ttl     = 10800
  records = [
    "v=spf1 include:_mailcust.gandi.net ?all",
    "google-site-verification=ADFZnYpkNQq6LmqCTn8BNoFOoTdwXjDFUSiq_D1eNkw",
  ]
}

# Gandi のウェブリダイレクト。
resource "aws_route53_record" "www" {
  count = var.manage_dns ? 1 : 0

  zone_id = local.zone_id
  name    = "www.${var.domain_name}"
  type    = "CNAME"
  ttl     = 10800
  records = ["webredir.vip.gandi.net."]
}

resource "aws_route53_record" "blog" {
  count = var.manage_dns ? 1 : 0

  zone_id = local.zone_id
  name    = "blog.${var.domain_name}"
  type    = "CNAME"
  ttl     = 10800
  records = ["blogs.vip.gandi.net."]
}

# Gandi のウェブメール。落とすとメール画面に入れなくなる。
resource "aws_route53_record" "webmail" {
  count = var.manage_dns ? 1 : 0

  zone_id = local.zone_id
  name    = "webmail.${var.domain_name}"
  type    = "CNAME"
  ttl     = 10800
  records = ["webmail.gandi.net."]
}

# --- 証明書の検証レコード ---------------------------------------------------
# NS 移管前は Gandi 側の手動レコードで検証される。移管後は自動更新のためにこちらが要る。
# App Runner の証明書検証レコードは apprunner_validation_records で別に引き継ぐ
# (App Runner を撤去するまで証明書の更新に必要)。
resource "aws_route53_record" "acm_validation" {
  for_each = var.manage_dns && var.enable_custom_domain ? {
    for o in aws_acm_certificate.this[0].domain_validation_options : o.domain_name => o
  } : {}

  zone_id         = local.zone_id
  name            = each.value.resource_record_name
  type            = each.value.resource_record_type
  records         = [each.value.resource_record_value]
  ttl             = 60
  allow_overwrite = true
}

#
# Gandi のゾーンには同じ検証レコードが「相対名」と「FQDN を相対名の欄に入れてしまったもの」の
# 2 通りで登録されており、後者は ....dev.vsmarketplacebadges.dev という二重ドメインになっている。
# 無害だが不要なので、移管するのは相対名の方だけ。
resource "aws_route53_record" "apprunner_validation" {
  for_each = var.manage_dns ? var.apprunner_validation_records : {}

  zone_id = local.zone_id
  name    = "${each.key}.${var.domain_name}"
  type    = "CNAME"
  ttl     = 10800
  records = [each.value]
}
