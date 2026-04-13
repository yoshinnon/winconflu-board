# ============================================================
# WinConflu.NET + Boards — Terraform Outputs
# ============================================================

output "resource_group_name" {
  description = "リソースグループ名"
  value       = azurerm_resource_group.main.name
}

output "app_service_url" {
  description = "App Service の URL"
  value       = "https://${azurerm_windows_web_app.main.default_hostname}"
}

output "app_service_staging_url" {
  description = "Staging スロットの URL"
  value       = "https://${azurerm_windows_web_app_slot.staging.default_hostname}"
}

output "app_service_principal_id" {
  description = "App Service Managed Identity の Principal ID（SQL USER 作成時に使用）"
  value       = azurerm_windows_web_app.main.identity[0].principal_id
}

output "sql_server_fqdn" {
  description = "SQL Server の完全修飾ドメイン名"
  value       = azurerm_mssql_server.main.fully_qualified_domain_name
}

output "sql_database_name" {
  description = "SQL Database 名"
  value       = azurerm_mssql_database.main.name
}

output "storage_account_name" {
  description = "Storage Account 名"
  value       = azurerm_storage_account.main.name
}

output "storage_blob_endpoint" {
  description = "Blob Storage エンドポイント"
  value       = azurerm_storage_account.main.primary_blob_endpoint
}

output "key_vault_uri" {
  description = "Key Vault URI"
  value       = azurerm_key_vault.main.vault_uri
}

output "application_insights_connection_string" {
  description = "Application Insights 接続文字列"
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "signalr_connection_string" {
  description = "SignalR Service 接続文字列"
  value       = azurerm_signalr_service.main.primary_connection_string
  sensitive   = true
}

# ── デプロイ後の手動作業ガイダンス ──────────────────────────
output "post_deploy_instructions" {
  description = "Terraform apply 後に実施する手動作業"
  value       = <<-EOT
    ========================================================
    デプロイ後の設定手順
    ========================================================

    1. SQL Database に Managed Identity ユーザーを追加:
       SQL Server に接続して以下を実行:

       CREATE USER [${azurerm_windows_web_app.main.name}] FROM EXTERNAL PROVIDER;
       ALTER ROLE db_datareader ADD MEMBER [${azurerm_windows_web_app.main.name}];
       ALTER ROLE db_datawriter ADD MEMBER [${azurerm_windows_web_app.main.name}];
       ALTER ROLE db_ddladmin  ADD MEMBER [${azurerm_windows_web_app.main.name}];

    2. EF Core マイグレーション初回実行:
       dotnet ef database update --connection "..."

    3. App Service で ARR Affinity が ON になっていることを確認:
       Azure Portal → App Service → 構成 → 全般設定 → ARR アフィニティ: ON

    4. WebSocket が有効になっていることを確認:
       Azure Portal → App Service → 構成 → 全般設定 → Web ソケット: ON
    ========================================================
  EOT
}
