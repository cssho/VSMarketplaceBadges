output "cloudfront_domain_name" {
  description = "CloudFront の既定ドメイン。DNS 切り替え前の動作確認はこの URL で行う。"
  value       = aws_cloudfront_distribution.badges.domain_name
}

output "cloudfront_distribution_id" {
  description = "キャッシュ無効化 (aws cloudfront create-invalidation) に使う ID。"
  value       = aws_cloudfront_distribution.badges.id
}

output "lambda_function_name" {
  description = "GitHub Actions の LAMBDA_FUNCTION_NAME に設定する値。"
  value       = aws_lambda_function.this.function_name
}

output "lambda_alias_name" {
  description = "CloudFront が向いているエイリアス。CI はデプロイのたびにこれを付け替える。"
  value       = aws_lambda_alias.live.name
}

output "lambda_function_url" {
  description = "Function URL。AWS_IAM 認証なので直接叩いても 403 になるのが正しい状態。"
  value       = aws_lambda_function_url.live.function_url
}

output "log_group_name" {
  description = "CloudWatch Logs のロググループ。"
  value       = aws_cloudwatch_log_group.lambda.name
}

output "github_actions_role_arn" {
  description = "GitHub Actions の変数 AWS_DEPLOY_ROLE_ARN に設定する値。"
  value       = aws_iam_role.github_actions.arn
}

output "acm_certificate_arn" {
  description = "CloudFront に付けた ACM 証明書 (us-east-1)。独自ドメイン未使用なら null。"
  value       = var.enable_custom_domain ? aws_acm_certificate_validation.this[0].certificate_arn : null
}

output "acm_validation_records" {
  description = <<-EOT
    ACM の DNS 検証用レコード。NS 移管前は Gandi に手で登録する必要がある。
  EOT
  value = var.enable_custom_domain ? {
    for o in aws_acm_certificate.this[0].domain_validation_options :
    o.resource_record_name => o.resource_record_value
  } : {}
}

output "route53_zone_id" {
  description = "作成した Route 53 ホストゾーン ID。レコードの突き合わせに使う。"
  value       = var.manage_dns ? aws_route53_zone.main[0].zone_id : null
}

output "route53_name_servers" {
  description = <<-EOT
    Gandi のレジストラ設定に登録する NS。これを反映した時点が実際の切り替えになる。
  EOT
  value       = var.manage_dns ? aws_route53_zone.main[0].name_servers : null
}

output "cloudfront_origin" {
  description = "現在 CloudFront が向いているオリジン。切り戻し状態の確認用。"
  value       = local.origin_host
}
