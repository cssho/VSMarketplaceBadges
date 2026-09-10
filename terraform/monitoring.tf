# ----------------------------------------------------------------------------
# 監視
#
# 移行前は App Runner 任せでアラームが一切無く、壊れても誰も気づけない状態だった。
# バッジは CloudFront に 1 時間キャッシュされるため、オリジンが死んでもしばらくは
# 正常に見えてしまう。異常が「見えない」時間が長い構成なので、通知は必須。
#
# CloudWatch アラームは同一リージョンの SNS トピックしか叩けない。CloudFront のメトリクスは
# us-east-1 にしか出ないため、トピックを 2 リージョンに持つ必要がある。
# ----------------------------------------------------------------------------

resource "aws_sns_topic" "alerts" {
  name = "${var.function_name}-alerts"
}

resource "aws_sns_topic" "alerts_us_east_1" {
  provider = aws.us_east_1
  name     = "${var.function_name}-alerts"
}

# メール購読は確認メールのリンクを踏むまで Pending のまま。届いたら承認すること。
resource "aws_sns_topic_subscription" "email" {
  count = var.alarm_email == "" ? 0 : 1

  topic_arn = aws_sns_topic.alerts.arn
  protocol  = "email"
  endpoint  = var.alarm_email
}

resource "aws_sns_topic_subscription" "email_us_east_1" {
  count    = var.alarm_email == "" ? 0 : 1
  provider = aws.us_east_1

  topic_arn = aws_sns_topic.alerts_us_east_1.arn
  protocol  = "email"
  endpoint  = var.alarm_email
}

# ----------------------------------------------------------------------------
# Lambda
# ----------------------------------------------------------------------------

# ハンドラーが落ちた・タイムアウトした回数。アプリが返す HTTP 500 はここに計上されない
# (ハンドラーとしては正常終了するため) ので、鳴ったら本当に異常。しきい値は 1 でよい。
resource "aws_cloudwatch_metric_alarm" "lambda_errors" {
  alarm_name          = "${var.function_name}-lambda-errors"
  alarm_description   = "Lambda handler failures (crash or timeout)"
  namespace           = "AWS/Lambda"
  metric_name         = "Errors"
  dimensions          = { FunctionName = aws_lambda_function.this.function_name }
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"

  # 呼び出しが無い時間帯にデータ欠損で ALARM にならないようにする。
  treat_missing_data = "notBreaching"
  alarm_actions      = [aws_sns_topic.alerts.arn]
  ok_actions         = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "lambda_throttles" {
  alarm_name          = "${var.function_name}-lambda-throttles"
  alarm_description   = "Lambda throttled (concurrency limit)"
  namespace           = "AWS/Lambda"
  metric_name         = "Throttles"
  dimensions          = { FunctionName = aws_lambda_function.this.function_name }
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"

  treat_missing_data = "notBreaching"
  alarm_actions      = [aws_sns_topic.alerts.arn]
  ok_actions         = [aws_sns_topic.alerts.arn]
}

# タイムアウト (var.lambda_timeout) に迫っている実行を捕まえる。Polly の予算を超えて
# 上流が詰まっている兆候なので、実際にタイムアウトする前に気づきたい。
resource "aws_cloudwatch_metric_alarm" "lambda_duration" {
  alarm_name          = "${var.function_name}-lambda-duration"
  alarm_description   = "Lambda duration approaching the ${var.lambda_timeout}s timeout"
  namespace           = "AWS/Lambda"
  metric_name         = "Duration"
  dimensions          = { FunctionName = aws_lambda_function.this.function_name }
  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 2
  threshold           = var.lambda_timeout * 1000 * 0.8
  comparison_operator = "GreaterThanThreshold"

  treat_missing_data = "notBreaching"
  alarm_actions      = [aws_sns_topic.alerts.arn]
  ok_actions         = [aws_sns_topic.alerts.arn]
}

# ----------------------------------------------------------------------------
# CloudFront (メトリクスは us-east-1 にしか出ない)
# ----------------------------------------------------------------------------

# 4xx は監視しない。未知のバッジタイプに対する 400 が正常系として常時出るため。
resource "aws_cloudwatch_metric_alarm" "cloudfront_5xx" {
  provider = aws.us_east_1

  alarm_name        = "${var.function_name}-cloudfront-5xx"
  alarm_description = "CloudFront 5xx rate is high (origin failing)"
  namespace         = "AWS/CloudFront"
  metric_name       = "5xxErrorRate"
  dimensions = {
    DistributionId = aws_cloudfront_distribution.badges.id
    Region         = "Global"
  }
  statistic           = "Average"
  period              = 300
  evaluation_periods  = 2
  threshold           = 5
  comparison_operator = "GreaterThanThreshold"

  treat_missing_data = "notBreaching"
  alarm_actions      = [aws_sns_topic.alerts_us_east_1.arn]
  ok_actions         = [aws_sns_topic.alerts_us_east_1.arn]
}

# ----------------------------------------------------------------------------
# コスト
#
# App Runner からの移行はコスト削減が目的だったので、想定を超えたら気づけるようにしておく。
# 無料枠に収まる想定なので、しきい値は低くてよい。
# ----------------------------------------------------------------------------
resource "aws_budgets_budget" "monthly" {
  count = var.alarm_email == "" ? 0 : 1

  name         = "${var.function_name}-monthly"
  budget_type  = "COST"
  limit_amount = var.monthly_budget_usd
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  # 実績が閾値を超えたとき。
  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 80
    threshold_type             = "PERCENTAGE"
    notification_type          = "ACTUAL"
    subscriber_email_addresses = [var.alarm_email]
  }

  # 月末予測が超えそうなとき。実績より早く気づける。
  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED"
    subscriber_email_addresses = [var.alarm_email]
  }
}
