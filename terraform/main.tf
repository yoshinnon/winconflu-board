# ============================================================
# WinConflu.NET + Boards — メインインフラ構成
# ============================================================

locals {
  prefix = "${var.project_name}-${var.environment}"

  common_tags = merge(var.tags, {
    Project     = "WinConflu.NET"
    Environment = var.environment
    ManagedBy   = "Terraform"
  })
}

# ── Resource Group ───────────────────────────────────────────
resource "azurerm_resource_group" "main" {
  name     = "${local.prefix}-rg"
  location = var.location
  tags     = local.common_tags
}

# ── Log Analytics Workspace（監視の基盤） ─────────────────────
resource "azurerm_log_analytics_workspace" "main" {
  name                = "${local.prefix}-law"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = local.common_tags
}

# ── Application Insights ──────────────────────────────────────
resource "azurerm_application_insights" "main" {
  name                = "${local.prefix}-ai"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.common_tags
}

# ── Key Vault（シークレット・接続文字列管理） ─────────────────
data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "main" {
  name                       = "${local.prefix}-kv"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  enable_rbac_authorization  = true
  purge_protection_enabled   = true
  soft_delete_retention_days = 90
  tags                       = local.common_tags
}

# Terraform 実行者に Key Vault Administrator 権限を付与
resource "azurerm_role_assignment" "kv_admin_terraform" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

# ── Azure SQL Server ──────────────────────────────────────────
resource "azurerm_mssql_server" "main" {
  name                         = "${local.prefix}-sql"
  resource_group_name          = azurerm_resource_group.main.name
  location                     = azurerm_resource_group.main.location
  version                      = "12.0"
  administrator_login          = var.sql_admin_login
  administrator_login_password = var.sql_admin_password
  minimum_tls_version          = "1.2"

  # Entra ID 管理者設定（パスワードレス認証の基盤）
  azuread_administrator {
    login_username = "AzureAD Admin"
    object_id      = data.azurerm_client_config.current.object_id
    tenant_id      = var.entra_tenant_id
  }

  tags = local.common_tags
}

# SQL Server 監査設定
resource "azurerm_mssql_server_extended_auditing_policy" "main" {
  server_id                               = azurerm_mssql_server.main.id
  storage_endpoint                        = azurerm_storage_account.main.primary_blob_endpoint
  storage_account_access_key              = azurerm_storage_account.main.primary_access_key
  storage_account_access_key_is_secondary = false
  retention_in_days                       = 90
}

# ── Azure SQL Database ────────────────────────────────────────
resource "azurerm_mssql_database" "main" {
  name           = "${local.prefix}-db"
  server_id      = azurerm_mssql_server.main.id
  sku_name       = var.sql_database_sku
  max_size_gb    = var.sql_max_size_gb
  zone_redundant = var.environment == "prod" ? true : false

  # 自動バックアップ: ポイントインタイムリストア
  short_term_retention_policy {
    retention_days           = 35
    backup_interval_in_hours = 12
  }

  long_term_retention_policy {
    weekly_retention  = "P4W"
    monthly_retention = "P12M"
    yearly_retention  = "P5Y"
    week_of_year      = 1
  }

  tags = local.common_tags
}

# SQL Server ファイアウォール：Azure サービスからのアクセス許可
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# ── Azure Blob Storage（Wiki画像・添付ファイル） ──────────────
resource "random_string" "storage_suffix" {
  length  = 6
  special = false
  upper   = false
}

resource "azurerm_storage_account" "main" {
  name                     = "${var.project_name}${var.environment}${random_string.storage_suffix.result}"
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = var.storage_replication_type
  min_tls_version          = "TLS1_2"

  blob_properties {
    versioning_enabled = true
    delete_retention_policy {
      days = 30
    }
    container_delete_retention_policy {
      days = 30
    }
  }

  tags = local.common_tags
}

# 添付ファイル用コンテナ
resource "azurerm_storage_container" "attachments" {
  name                  = "attachments"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

# Wiki画像用コンテナ
resource "azurerm_storage_container" "wiki_images" {
  name                  = "wiki-images"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

# ── App Service Plan ──────────────────────────────────────────
resource "azurerm_service_plan" "main" {
  name                = "${local.prefix}-plan"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  os_type             = "Windows"    # Blazor Server + Windows認証
  sku_name            = var.app_service_sku
  worker_count        = var.app_service_worker_count
  tags                = local.common_tags
}

# ── App Service（Blazor Server 本体） ─────────────────────────
resource "azurerm_windows_web_app" "main" {
  name                = "${local.prefix}-app"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  service_plan_id     = azurerm_service_plan.main.id
  https_only          = true

  site_config {
    websockets_enabled    = true   # SignalR / Blazor Server 必須
    always_on             = true
    http2_enabled         = true
    minimum_tls_version   = "1.2"
    ftps_state            = "Disabled"
    use_32_bit_worker     = false

    application_stack {
      current_stack  = "dotnet"
      dotnet_version = var.dotnet_version   # "v10.0"
    }

    # ARR Affinity はポータル設定で On（Terraform では app_settings で制御しない）
  }

  # Managed Identity を有効化（Key Vault / SQL / Blob へのパスワードレスアクセス）
  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    # Application Insights
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.main.connection_string
    "ApplicationInsightsAgent_EXTENSION_VERSION" = "~3"

    # SignalR Service（スケールアウト時）
    "Azure__SignalR__ConnectionString" = azurerm_signalr_service.main.primary_connection_string

    # Key Vault 参照（シークレットは直接書かず KV 参照）
    "KeyVaultUri" = azurerm_key_vault.main.vault_uri

    # Blob Storage（エンドポイントのみ、認証はManaged Identity）
    "Storage__BlobEndpoint" = azurerm_storage_account.main.primary_blob_endpoint

    # SQL Server（Managed Identity 認証のため接続文字列にパスワード不要）
    "ConnectionStrings__DefaultConnection" = (
      "Server=${azurerm_mssql_server.main.fully_qualified_domain_name};" +
      "Database=${azurerm_mssql_database.main.name};" +
      "Authentication=Active Directory Default;Encrypt=True;"
    )

    # パフォーマンス最適化
    "WEBSITE_RUN_FROM_PACKAGE" = "1"
  }

  logs {
    application_logs {
      file_system_level = "Warning"
    }
    http_logs {
      retention_in_days = 7
    }
  }

  tags = local.common_tags

  depends_on = [
    azurerm_role_assignment.kv_admin_terraform
  ]
}

# ── Deployment Slot（Blue/Green デプロイ用） ─────────────────
resource "azurerm_windows_web_app_slot" "staging" {
  name           = "staging"
  app_service_id = azurerm_windows_web_app.main.id

  site_config {
    websockets_enabled  = true
    always_on           = true
    http2_enabled       = true
    minimum_tls_version = "1.2"
    ftps_state          = "Disabled"

    application_stack {
      current_stack  = "dotnet"
      dotnet_version = var.dotnet_version
    }
  }

  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    "APPLICATIONINSIGHTS_CONNECTION_STRING"      = azurerm_application_insights.main.connection_string
    "ApplicationInsightsAgent_EXTENSION_VERSION" = "~3"
    "Azure__SignalR__ConnectionString"           = azurerm_signalr_service.main.primary_connection_string
    "KeyVaultUri"                                = azurerm_key_vault.main.vault_uri
    "Storage__BlobEndpoint"                      = azurerm_storage_account.main.primary_blob_endpoint
    "ConnectionStrings__DefaultConnection" = (
      "Server=${azurerm_mssql_server.main.fully_qualified_domain_name};" +
      "Database=${azurerm_mssql_database.main.name};" +
      "Authentication=Active Directory Default;Encrypt=True;"
    )
    "WEBSITE_RUN_FROM_PACKAGE" = "1"
  }

  tags = local.common_tags
}

# ── Azure SignalR Service（1,000ユーザー対応スケールアウト） ──
resource "azurerm_signalr_service" "main" {
  name                = "${local.prefix}-signalr"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  sku {
    name     = var.signalr_sku
    capacity = var.signalr_capacity
  }

  service_mode                 = "Default"
  connectivity_logs_enabled    = true
  messaging_logs_enabled       = true
  live_trace_enabled           = true

  tags = local.common_tags
}

# ── Managed Identity への RBAC ロール付与 ────────────────────

# App Service → Key Vault Secrets User
resource "azurerm_role_assignment" "app_kv_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_windows_web_app.main.identity[0].principal_id
}

# Staging Slot → Key Vault Secrets User
resource "azurerm_role_assignment" "staging_kv_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_windows_web_app_slot.staging.identity[0].principal_id
}

# App Service → Storage Blob Data Contributor
resource "azurerm_role_assignment" "app_storage_blob" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_windows_web_app.main.identity[0].principal_id
}

# App Service → SQL DB の Managed Identity 認証は
# EF Core の初回 Migration 後に CREATE USER で付与する（outputs.tf 参照）

# ── Key Vault シークレット保存 ────────────────────────────────

resource "azurerm_key_vault_secret" "sql_password" {
  name         = "sql-admin-password"
  value        = var.sql_admin_password
  key_vault_id = azurerm_key_vault.main.id

  depends_on = [azurerm_role_assignment.kv_admin_terraform]
}

resource "azurerm_key_vault_secret" "storage_connection" {
  name         = "storage-connection-string"
  value        = azurerm_storage_account.main.primary_connection_string
  key_vault_id = azurerm_key_vault.main.id

  depends_on = [azurerm_role_assignment.kv_admin_terraform]
}

# ── Azure Update Manager（パッチ管理） ──────────────────────
# App Service はマネージドサービスのため OS パッチは Azure が自動適用。
# SQL Database も同様に自動メンテナンス。
# 追加の VM がある場合のみ以下を有効化。
#
# resource "azurerm_maintenance_configuration" "main" { ... }
