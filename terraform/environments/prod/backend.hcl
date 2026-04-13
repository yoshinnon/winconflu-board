# 本番環境 Terraform State バックエンド設定
# 使用方法: terraform init -backend-config=environments/prod/backend.hcl

resource_group_name  = "wcn-tfstate-rg"
storage_account_name = "wcntfstateprod"
container_name       = "tfstate"
key                  = "prod.terraform.tfstate"
