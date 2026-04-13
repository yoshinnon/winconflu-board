# ============================================================
# WinConflu.NET + Boards — Terraform 変数定義
# ============================================================

# ── 共通 ────────────────────────────────────────────────────

variable "project_name" {
  description = "プロジェクト識別子（リソース命名に使用）"
  type        = string
  default     = "wcn"
}

variable "environment" {
  description = "環境識別子: dev | staging | prod"
  type        = string
  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "environment は dev / staging / prod のいずれかにしてください。"
  }
}

variable "location" {
  description = "Azureリージョン"
  type        = string
  default     = "japaneast"
}

variable "tags" {
  description = "全リソースに付与する共通タグ"
  type        = map(string)
  default     = {}
}

# ── App Service ────────────────────────────────────────────

variable "app_service_sku" {
  description = "App Service Plan SKU（P1v3 / P2v3 / P3v3）"
  type        = string
  default     = "P2v3"
}

variable "app_service_worker_count" {
  description = "App Service のワーカー数（スケールアウト数）"
  type        = number
  default     = 2
}

variable "dotnet_version" {
  description = ".NET バージョン（App Service の dotnet_version 設定値）"
  type        = string
  default     = "v10.0"
}

# ── Azure SQL Database ────────────────────────────────────

variable "sql_admin_login" {
  description = "SQL Server 管理者ログイン名"
  type        = string
  default     = "wcnadmin"
  sensitive   = true
}

variable "sql_admin_password" {
  description = "SQL Server 管理者パスワード（Key Vault 管理推奨）"
  type        = string
  sensitive   = true
}

variable "sql_database_sku" {
  description = "Azure SQL Database SKU"
  type        = string
  default     = "GP_Gen5_4"
  # 開発: Basic / S1 、本番: GP_Gen5_4 (General Purpose 4vCore)
}

variable "sql_max_size_gb" {
  description = "SQL Database の最大サイズ (GB)"
  type        = number
  default     = 100
}

# ── Azure SignalR ──────────────────────────────────────────

variable "signalr_sku" {
  description = "SignalR Service SKU（Free_F1 / Standard_S1）"
  type        = string
  default     = "Standard_S1"
}

variable "signalr_capacity" {
  description = "SignalR ユニット数"
  type        = number
  default     = 1
}

# ── Azure Blob Storage ────────────────────────────────────

variable "storage_replication_type" {
  description = "ストレージ冗長性（LRS / ZRS / GRS）"
  type        = string
  default     = "ZRS"
}

# ── Entra ID / AD 連携 ────────────────────────────────────

variable "entra_tenant_id" {
  description = "Azure Entra ID テナント ID"
  type        = string
  sensitive   = true
}

variable "allowed_ad_group_ids" {
  description = "アプリへのアクセスを許可するADグループIDのリスト"
  type        = list(string)
  default     = []
}

# ── 監視 ────────────────────────────────────────────────────

variable "log_retention_days" {
  description = "Log Analytics ワークスペースのログ保持日数"
  type        = number
  default     = 90
}
