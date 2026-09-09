terraform {
  required_version = ">= 1.6"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    archive = {
      source  = "hashicorp/archive"
      version = "~> 2.4"
    }
  }

  # バックエンドは環境ごとに異なるため、-backend-config で与える。
  # 例: terraform init -backend-config=backend.hcl
  backend "s3" {}
}

provider "aws" {
  region = var.region

  default_tags {
    tags = var.tags
  }
}

# CloudFront が使う ACM 証明書は us-east-1 にしか置けない。
# 配信リージョン (ap-northeast-1) とは別のプロバイダーが必要になる。
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"

  default_tags {
    tags = var.tags
  }
}
