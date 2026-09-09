locals {
  # フェーズ 2 以降でのみ DNS を扱う。App Runner 向きのレコードを引き継ぐ場合は
  # apprunner_service_url を与える。空のままなら切り替え前のレコードには一切触らない
  # (既存レコードを Terraform の管理外に置いたままにする、いちばん安全な既定)。
  manage_dns_record = var.enable_custom_domain && (var.enable_dns_cutover || var.apprunner_service_url != "")
}

# ----------------------------------------------------------------------------
# ACM 証明書 (us-east-1)
#
# フェーズ 2 で作る。証明書の発行と CloudFront への別名付与だけでは DNS は動かないので、
# この時点では本番トラフィックは App Runner に流れ続ける。
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

resource "aws_route53_record" "cert_validation" {
  for_each = var.enable_custom_domain ? {
    for dvo in aws_acm_certificate.this[0].domain_validation_options :
    dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  } : {}

  zone_id         = var.route53_zone_id
  name            = each.value.name
  type            = each.value.type
  records         = [each.value.record]
  ttl             = 60
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "this" {
  count    = var.enable_custom_domain ? 1 : 0
  provider = aws.us_east_1

  certificate_arn         = aws_acm_certificate.this[0].arn
  validation_record_fqdns = [for r in aws_route53_record.cert_validation : r.fqdn]
}

# ----------------------------------------------------------------------------
# 配信レコード
#
# enable_dns_cutover がこの移行の実スイッチ。true で CloudFront、false で App Runner。
# App Runner は削除せず残してあるので、false に戻して apply すれば切り戻せる。
# TTL 60 秒にしてあるのは、切り戻しの反映を待たされないため。
# ----------------------------------------------------------------------------

# 主レコード。切り替え前は App Runner への CNAME、切り替え後は CloudFront への A ALIAS。
#
# CNAME と A を別リソースに分けてはいけない。Route 53 は同名に CNAME と他の型を共存させられず、
# かつ Terraform は依存関係のない destroy と create の順序を保証しないため、フラグを倒した
# 瞬間に「CNAME が残ったまま A を作る」順序になって
#   RRSet of type A with DNS name ... is not permitted because a conflicting RRSet exists
# で apply が落ちる。allow_overwrite は同名かつ同型にしか効かないので救済にならない。
# 1 リソースにまとめておけば type の変更は同一リソースの置換となり、destroy → create の
# 順序が保証される。切り戻し (フェーズ 3 → 2) でも同じ理屈で守られる。
#
# App Runner の既定ドメインは ALIAS のターゲットにできないので CNAME を使う。
# そのため domain_name はサブドメインである必要がある (Zone Apex は CNAME 不可)。
resource "aws_route53_record" "badges" {
  count = local.manage_dns_record ? 1 : 0

  zone_id = var.route53_zone_id
  name    = var.domain_name
  type    = var.enable_dns_cutover ? "A" : "CNAME"

  # ALIAS レコードに ttl / records は指定できないため、切り替え後は null にする。
  ttl     = var.enable_dns_cutover ? null : 60
  records = var.enable_dns_cutover ? null : [var.apprunner_service_url]

  # 既存の App Runner 向きレコードを Terraform 管理下に引き取る。
  allow_overwrite = true

  dynamic "alias" {
    for_each = var.enable_dns_cutover ? [1] : []
    content {
      name                   = aws_cloudfront_distribution.badges.domain_name
      zone_id                = aws_cloudfront_distribution.badges.hosted_zone_id
      evaluate_target_health = false
    }
  }
}

# IPv6。これも CNAME と共存できないため、主レコードが A に置き換わった「あと」に作る必要がある。
# depends_on で順序を固定する (destroy はこの逆順になるので、切り戻し時は AAAA が先に消える)。
resource "aws_route53_record" "badges_ipv6" {
  count = local.manage_dns_record && var.enable_dns_cutover ? 1 : 0

  zone_id         = var.route53_zone_id
  name            = var.domain_name
  type            = "AAAA"
  allow_overwrite = true

  alias {
    name                   = aws_cloudfront_distribution.badges.domain_name
    zone_id                = aws_cloudfront_distribution.badges.hosted_zone_id
    evaluate_target_health = false
  }

  depends_on = [aws_route53_record.badges]
}
