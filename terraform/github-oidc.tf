# ----------------------------------------------------------------------------
# GitHub Actions からの OIDC 認証
#
# 長期のアクセスキー (IAM ユーザー for-github-actions) を GitHub Secrets に置く代わりに、
# ワークフロー実行ごとに発行される短命の OIDC トークンでロールを引き受ける。
# 漏洩しても失効済みで、ローテーションも不要になる。
# ----------------------------------------------------------------------------

resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]

  # AWS は GitHub の OIDC について自前の CA で検証するようになったため実質参照されないが、
  # API が値を要求するので既知のサムプリントを渡しておく。
  thumbprint_list = [
    "6938fd4d98bab03faadb97b34396831e3780aea1",
    "1c58a3a8518e8759bf075b76b750d4f2df264fcd",
  ]
}

data "aws_iam_policy_document" "github_actions_assume_role" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
    }

    # aud を固定しないと、別の相手向けに発行されたトークンを受け入れてしまう。
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # sub をリポジトリと ref まで絞る。ここを緩めると、同じ GitHub OIDC を使う
    # 世界中の任意のリポジトリからこのロールを引き受けられてしまうので必ず固定する。
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = [for r in var.github_deploy_refs : "repo:${var.github_repository}:ref:${r}"]
    }
  }
}

resource "aws_iam_role" "github_actions" {
  name = "${var.function_name}-github-actions"

  # IAM の description は ASCII / Latin-1 しか受け付けないため、ここだけ英語で書く
  # (日本語を入れると CreateRole が ValidationError で失敗する)。
  description        = "Deploy role assumed by GitHub Actions (deploy-lambda) via OIDC"
  assume_role_policy = data.aws_iam_policy_document.github_actions_assume_role.json
}

# deploy-lambda.yml が実際に叩く API だけに絞る。
data "aws_iam_policy_document" "github_actions_deploy" {
  # コード差し替えとバージョン発行。waiter (function-updated /
  # published-version-active) が GetFunctionConfiguration を叩くので併せて許可する。
  statement {
    sid = "DeployFunction"
    actions = [
      "lambda:UpdateFunctionCode",
      "lambda:PublishVersion",
      "lambda:GetFunction",
      "lambda:GetFunctionConfiguration",
    ]
    resources = [
      aws_lambda_function.this.arn,
      "${aws_lambda_function.this.arn}:*",
    ]
  }

  # CloudFront が向いているエイリアスの付け替え。
  #
  # UpdateAlias / GetAlias は「エイリアスの ARN」ではなく**修飾なしの関数 ARN**に対して
  # 認可される。最小権限のつもりでエイリアス ARN だけを書くと
  #   not authorized to perform: lambda:UpdateAlias on resource:
  #   arn:aws:lambda:...:function:vsmarketplace-badges
  # で拒否されるため、両方を並べる必要がある。
  statement {
    sid = "ShiftAlias"
    actions = [
      "lambda:UpdateAlias",
      "lambda:GetAlias",
    ]
    resources = [
      aws_lambda_function.this.arn,
      "${aws_lambda_function.this.arn}:${aws_lambda_alias.live.name}",
    ]
  }

  # デプロイ直後にバッジを更新するためのキャッシュ無効化。
  statement {
    sid       = "InvalidateCache"
    actions   = ["cloudfront:CreateInvalidation"]
    resources = [aws_cloudfront_distribution.badges.arn]
  }
}

resource "aws_iam_role_policy" "github_actions_deploy" {
  name   = "deploy-lambda"
  role   = aws_iam_role.github_actions.id
  policy = data.aws_iam_policy_document.github_actions_deploy.json
}
