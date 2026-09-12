locals {
  # aws_lambda_function_url は "https://xxxx.lambda-url.ap-northeast-1.on.aws/" を返すが、
  # CloudFront のオリジンにはスキームと末尾スラッシュを除いたホスト名だけを渡す。
  function_url_host = replace(replace(aws_lambda_function_url.live.function_url, "https://", ""), "/", "")

  # 切り戻しはオリジンの差し替えで行う (var.rollback_to_apprunner)。DNS は常に CloudFront を
  # 指したままなので、切り替えも切り戻しも DNS の TTL に左右されない。
  origin_host = var.rollback_to_apprunner ? var.apprunner_service_url : local.function_url_host

  # App Runner は公開エンドポイントなので OAC で署名しない。というより、署名すると
  # App Runner 側が解釈できない Authorization ヘッダーが付くだけで害になる。
  origin_access_control = var.rollback_to_apprunner ? null : aws_cloudfront_origin_access_control.lambda.id
}

resource "aws_cloudfront_origin_access_control" "lambda" {
  name                              = "${var.function_name}-oac"
  description                       = "CloudFront から Lambda Function URL を SigV4 署名して呼ぶ"
  origin_access_control_origin_type = "lambda"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# ----------------------------------------------------------------------------
# キャッシュポリシー
#
# ★ query_string_behavior = "all" は必須。
#   BadgeController は Request.QueryString をそのまま shields.io に転送しており
#   (?color=blue などのカスタマイズ)、クエリ文字列をキャッシュキーから外すと
#   すべてのバッジが最初にキャッシュされた 1 枚に化ける。
#
# TTL はオリジンの [ResponseCache(Duration = 3600)] に合わせてある。
# ----------------------------------------------------------------------------
resource "aws_cloudfront_cache_policy" "badges" {
  name        = "${var.function_name}-cache"
  comment     = "Badge responses keyed by full query string"
  min_ttl     = 0
  default_ttl = 3600
  max_ttl     = 86400

  parameters_in_cache_key_and_forwarded_to_origin {
    enable_accept_encoding_gzip   = true
    enable_accept_encoding_brotli = true

    cookies_config {
      cookie_behavior = "none"
    }

    # Host を転送すると Function URL の SigV4 検証が壊れる。
    # このアプリはリクエストヘッダーを一切見ないので none でよい。
    headers_config {
      header_behavior = "none"
    }

    query_strings_config {
      query_string_behavior = "all"
    }
  }
}

# ----------------------------------------------------------------------------
# セキュリティヘッダー
#
# 配信全体に効かせて問題のないものだけを置く。CSP はここに入れない。
# ドキュメントページ (wwwroot/index.html) が jQuery・Bootstrap・Twitter ウィジェットと
# インラインスクリプトを使っており、全体に厳格な CSP をかけると壊れるため。
# バッジ応答向けの CSP は BadgeController が個別に付ける。
# ----------------------------------------------------------------------------
resource "aws_cloudfront_response_headers_policy" "security" {
  name    = "${var.function_name}-security-headers"
  comment = "Security headers safe to apply to every response"

  security_headers_config {
    # 上流の応答を image/svg+xml で返しているため、ブラウザに型を推測させない。
    content_type_options {
      override = true
    }

    frame_options {
      frame_option = "DENY"
      override     = true
    }

    # バッジは README から参照されるので、参照元の URL を丸ごと送らない。
    referrer_policy {
      referrer_policy = "strict-origin-when-cross-origin"
      override        = true
    }

    strict_transport_security {
      access_control_max_age_sec = 31536000
      include_subdomains         = true
      preload                    = false
      override                   = true
    }
  }

  custom_headers_config {
    # vscode.dev / github.dev は SharedArrayBuffer のために
    # Cross-Origin-Embedder-Policy: require-corp で配信されている。その文脈では
    # クロスオリジンの画像は CORP が付いていないと読み込みが拒否され、
    #   ERR_BLOCKED_BY_RESPONSE.NotSameOriginAfterDefaultedToSameOriginByCoep
    # でバッジが壊れる。img.shields.io や raw.githubusercontent.com も同じ値を返している。
    # https://github.com/cssho/VSMarketplaceBadges/issues/11
    items {
      header   = "Cross-Origin-Resource-Policy"
      value    = "cross-origin"
      override = true
    }
  }
}

resource "aws_cloudfront_distribution" "badges" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "VSMarketplaceBadges"
  price_class         = var.price_class
  default_root_object = "index.html"

  aliases = var.enable_custom_domain ? [var.domain_name] : []

  origin {
    origin_id                = "lambda"
    domain_name              = local.origin_host
    origin_access_control_id = local.origin_access_control

    custom_origin_config {
      http_port                = 80
      https_port               = 443
      origin_protocol_policy   = "https-only"
      origin_ssl_protocols     = ["TLSv1.2"]
      origin_read_timeout      = var.origin_read_timeout
      origin_keepalive_timeout = 5
    }
  }

  default_cache_behavior {
    target_origin_id       = "lambda"
    viewer_protocol_policy = "redirect-to-https"

    # バッジ配信は GET のみ。書き込み系メソッドはオリジンに届かせない。
    allowed_methods = ["GET", "HEAD"]
    cached_methods  = ["GET", "HEAD"]

    # SVG はテキストなので圧縮が効く。
    compress                   = true
    cache_policy_id            = aws_cloudfront_cache_policy.badges.id
    response_headers_policy_id = aws_cloudfront_response_headers_policy.security.id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = !var.enable_custom_domain
    acm_certificate_arn            = var.enable_custom_domain ? aws_acm_certificate_validation.this[0].certificate_arn : null
    ssl_support_method             = var.enable_custom_domain ? "sni-only" : null
    minimum_protocol_version       = var.enable_custom_domain ? "TLSv1.2_2021" : null
  }
}
