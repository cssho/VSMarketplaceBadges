data "aws_caller_identity" "current" {}

# ----------------------------------------------------------------------------
# デプロイ成果物のプレースホルダー
#
# 実際の ZIP は .github/workflows/deploy-lambda.yml が update-function-code で載せる。
# Terraform は「関数という器」だけを持ち、コードの中身は CI の持ち物という分担にしている。
# そのため aws_lambda_function 側で filename / source_code_hash を ignore_changes している。
# ----------------------------------------------------------------------------
data "archive_file" "placeholder" {
  type        = "zip"
  output_path = "${path.module}/.terraform-artifacts/placeholder.zip"

  source {
    filename = "placeholder.txt"
    content  = "Placeholder. The real package is deployed by GitHub Actions."
  }
}

# ----------------------------------------------------------------------------
# IAM
# ----------------------------------------------------------------------------
data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "lambda" {
  name               = "${var.function_name}-lambda"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

# ログを CloudWatch Logs に書く権限だけ。ログ出力先を S3 から stdout に変えたので、
# App Runner 時代に必要だった S3 への PutObject 権限はもう要らない。
resource "aws_iam_role_policy_attachment" "lambda_basic" {
  role       = aws_iam_role.lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

# ----------------------------------------------------------------------------
# Lambda
# ----------------------------------------------------------------------------

# 関数より先に作る。Lambda に自動生成させると保持期間が「無期限」になり、
# 少額とはいえ止まらない課金が積み上がるため。
resource "aws_cloudwatch_log_group" "lambda" {
  name              = "/aws/lambda/${var.function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "this" {
  function_name = var.function_name
  role          = aws_iam_role.lambda.arn

  # Amazon.Lambda.AspNetCoreServer.Hosting を使う場合、ハンドラーはアセンブリ名だけでよい。
  # Startup.cs の AddAWSLambdaHosting が Kestrel を Lambda ランタイム API に差し替える。
  runtime = "dotnet8"
  handler = "VSMarketplaceBadges"

  filename         = data.archive_file.placeholder.output_path
  source_code_hash = data.archive_file.placeholder.output_base64sha256

  memory_size = var.lambda_memory_size
  timeout     = var.lambda_timeout

  # SnapStart は「発行済みバージョン」に対して働く。$LATEST では効かないので、
  # CloudFront は下の live エイリアス経由でしか呼ばない。
  publish = true

  dynamic "snap_start" {
    for_each = var.enable_snapstart ? [1] : []
    content {
      apply_on = "PublishedVersions"
    }
  }

  environment {
    variables = {
      ASPNETCORE_ENVIRONMENT = "Production"
    }
  }

  depends_on = [
    aws_iam_role_policy_attachment.lambda_basic,
    aws_cloudwatch_log_group.lambda,
  ]

  lifecycle {
    # コードは CI が入れ替える。Terraform が plan のたびにプレースホルダーへ
    # 巻き戻そうとするのを止める。
    ignore_changes = [filename, source_code_hash]
  }
}

# CloudFront が向く先を固定するためのエイリアス。デプロイのたびに CI が
# 新しいバージョンを発行してこのエイリアスを付け替える。
resource "aws_lambda_alias" "live" {
  name             = "live"
  function_name    = aws_lambda_function.this.function_name
  function_version = aws_lambda_function.this.version

  lifecycle {
    ignore_changes = [function_version]
  }
}

resource "aws_lambda_function_url" "live" {
  function_name = aws_lambda_function.this.function_name
  qualifier     = aws_lambda_alias.live.name

  # 誰でも叩ける NONE ではなく AWS_IAM。CloudFront の OAC が SigV4 で署名するため、
  # Function URL を直接叩かれる経路が塞がれ、キャッシュを迂回されることもなくなる。
  authorization_type = "AWS_IAM"
}

resource "aws_lambda_permission" "cloudfront" {
  statement_id           = "AllowCloudFrontInvokeFunctionUrl"
  action                 = "lambda:InvokeFunctionUrl"
  function_name          = aws_lambda_function.this.function_name
  qualifier              = aws_lambda_alias.live.name
  principal              = "cloudfront.amazonaws.com"
  source_arn             = aws_cloudfront_distribution.badges.arn
  function_url_auth_type = "AWS_IAM"
}
