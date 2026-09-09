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

output "acm_certificate_arn" {
  description = "CloudFront に付けた ACM 証明書 (us-east-1)。独自ドメイン未使用なら null。"
  value       = var.enable_custom_domain ? aws_acm_certificate_validation.this[0].certificate_arn : null
}
