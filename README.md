# WinConflu.NET + Boards

Azure / Windows 環境に特化したゼロコンフィグ・ナレッジ＆タスク管理プラットフォーム。  
Confluence + Jira の主要機能を .NET 10 / Blazor Server / Tiptap でフルスタック実装。

Confluence + Jira はドキュメントデータが増えまくると、とてつもなく遅いシステムに成り下がりますが、Azure + Windows環境に特化させることで軽量化を図ったシステムです。
おまけにActive Directoryと連携することで、新規ユーザ追加時の管理者・ユーザの手間が減ります。

### 編集機能における懸念 
ConfluenceのWYSIWIG編集画面に似せるため、無理やりTiptap（Typescript）を編集画面として実装しています。
WYSIWIGにこだわらずMarkdown形式での編集画面でよければ、Typescriptの部分が丸ごとC#で統一・実装できるため、もっとシンプルになります。

---

## 機能概要

### Wiki（Confluence 相当）
- 無限階層ページ（SQL Server HierarchyId）・パンくず・子ページ一覧
- **Tiptap WYSIWYG エディタ**（スラッシュコマンド / バブルメニュー / テーブルリサイズ / InfoBox マクロ / IssueEmbed / @メンション）
- テンプレート機能（議事録・仕様書・日報・障害報告書・週次レポート 5種組み込み）
- インラインコメント（テキスト選択 → スレッド形式注釈 / 解決 / Outdated 検出）
- バージョン管理・差分表示（DiffPlex）・ロールバック
- SQL Server フルテキスト検索（CONTAINSTABLE）
- Markdig Markdown レンダリング（既存コンテンツの閲覧用）

### Boards（Jira 相当）
- カンバンボード（Todo / Doing / Done / Verified）
- IssueType（Task / Bug / Story / Epic / SubTask）・エピック階層・進捗集計
- ワークフロー管理（遷移ルール / AD グループロール制約 / コメント必須 / 自動化）
- ロードマップ（StartDate 対応ガントチャート / 4〜24週スライダー）
- チケット間依存関係（Blocks / IsBlockedBy / Relates）
- ストーリーポイント / 開始日

### M365 インテグレーション
- Teams プレゼンス（30秒キャッシュ / 最大 650 ユーザーバッチ取得）
- ADユーザー検索（Graph API / 顔写真付き）
- Outlook 予定表同期（期限付きチケット自動登録・Upsert）

### 通知・運用
- アプリ内通知 + 期限リマインダー（Hangfire 毎時 / 毎朝 9:00）
- 多段キャッシュ（Volatile 30s ～ Long 6h）
- DB インデックス断片化監視・自動再構築（毎週日曜 2:00）

---

## 技術スタック

| レイヤー | 技術 |
|---|---|
| フレームワーク | ASP.NET Core 10 / Blazor Server |
| UI | MudBlazor 9.x |
| WYSIWYG エディタ | Tiptap 2.x (TypeScript / Vite) |
| ORM | EF Core 10 + HierarchyId |
| DB | Azure SQL Database |
| Markdown | Markdig 0.x（閲覧用レンダリング専用）|
| 差分表示 | DiffPlex 1.x |
| バックグラウンドジョブ | Hangfire 1.x (SQL Server) |
| 認証 | Windows 統合認証 (Negotiate/Kerberos) |
| ストレージ | Azure Blob Storage |
| リアルタイム | Azure SignalR Service |
| M365 連携 | Microsoft Graph API v5.x |
| IaC | Terraform (azurerm ~> 4.0) |
| CI/CD | GitHub Actions (OIDC 認証) |

---

## セットアップ手順

### 1. 前提条件

- .NET 10 SDK
- Node.js 20+
- Azure CLI
- Terraform 1.6+

### 2. Terraform でインフラ構築

```bash
# State ストレージ（初回のみ）
az group create --name wcn-tfstate-rg --location japaneast
az storage account create --name wcntfstateprod \
  --resource-group wcn-tfstate-rg --sku Standard_LRS

cd terraform
terraform init -backend-config=environments/prod/backend.hcl
terraform apply \
  -var-file=environments/prod/terraform.tfvars \
  -var="sql_admin_password=<安全なパスワード>" \
  -var="entra_tenant_id=<テナントID>"
```

### 3. SQL Database に Managed Identity ユーザー追加

Azure Portal または sqlcmd でアプリの Managed Identity を登録：

```sql
CREATE USER [<App Service 名>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader  ADD MEMBER [<App Service 名>];
ALTER ROLE db_datawriter  ADD MEMBER [<App Service 名>];
ALTER ROLE db_ddladmin    ADD MEMBER [<App Service 名>];
```

### 4. EF Core マイグレーション

```bash
cd src/WinConflu
dotnet ef database update
```

マイグレーション適用順：

| ファイル | 内容 |
|---|---|
| 20250101_InitialCreate | 全テーブル + FTS インデックス |
| 20250201_AddTemplatesAndAnnotations | テンプレート・インラインコメント |
| 20250301_AddWorkflowAndIssueHierarchy | ワークフロー・エピック階層 |
| 20250401_AddNotifications | アプリ内通知 |
| 20250501_AddIssueExtendedFields | StartDate / StoryPoints / Progress |
| 20250601_AddPageContentFormat | WYSIWYG 対応（ContentFormat / ContentText）|

### 5. フルテキスト検索インデックス作成

```sql
CREATE FULLTEXT CATALOG wcn_ftcat AS DEFAULT;

CREATE FULLTEXT INDEX ON Pages(Title, Content, ContentText)
  KEY INDEX PK_Pages ON wcn_ftcat WITH CHANGE_TRACKING AUTO;

CREATE FULLTEXT INDEX ON Issues(Title, Description)
  KEY INDEX PK_Issues ON wcn_ftcat WITH CHANGE_TRACKING AUTO;
```

### 6. Tiptap エディタのビルド

```bash
cd src/WinConflu/wwwroot/editor
npm install
npm run build
# → wwwroot/dist/wcn-editor.iife.js が生成される
```

> `dotnet build` 時に自動実行されます（`WinConflu.csproj` の `BuildTiptap` ターゲット）。

### 7. GitHub Actions シークレット設定

| シークレット | 内容 |
|---|---|
| `AZURE_CLIENT_ID` | OIDC Service Principal クライアント ID |
| `AZURE_TENANT_ID` | Entra ID テナント ID |
| `AZURE_SUBSCRIPTION_ID` | サブスクリプション ID |
| `SQL_ADMIN_PASSWORD` | SQL Server 管理者パスワード |

### 8. ローカル開発

```bash
# Azurite（ローカル Blob Storage エミュレータ）
npx azurite --silent --location ./azurite-data

# 開発サーバー起動
cd src/WinConflu
dotnet watch run
```

---

## アーキテクチャ概要

詳細は [SYSTEM_DESIGN.md](./SYSTEM_DESIGN.md) を参照。

```
Browser (Blazor Server + Tiptap)
    │ SignalR
    ▼
Azure App Service (Windows / P2v3 / .NET 10)
    │
    ├── Azure SQL Database   (EF Core / HierarchyId / FTS)
    ├── Azure Blob Storage   (添付ファイル)
    ├── Azure SignalR Service (1,000 接続スケール)
    ├── Azure Key Vault       (シークレット)
    └── Microsoft Graph API  (Teams / Outlook / AD)
```

---

## ディレクトリ構造

```
winconflu/
├── SYSTEM_DESIGN.md           # システム設計書
├── README.md                  # 本ファイル
├── .github/workflows/
│   ├── deploy.yml             # アプリデプロイ（Staging → Swap）
│   └── terraform.yml          # インフラ管理
├── terraform/                 # IaC
│   └── environments/
│       ├── prod/
│       └── dev/
└── src/WinConflu/
    ├── Components/
    │   ├── Pages/             # Wiki / Kanban / Roadmap / Search / Settings
    │   └── Shared/            # TiptapEditor / NotificationBell / Diff 等
    ├── Data/AppDbContext.cs
    ├── Migrations/            # 6 マイグレーション
    ├── Models/                # エンティティ定義
    ├── Services/              # ビジネスロジック（18 サービス）
    │   └── Graph/             # M365 連携
    └── wwwroot/
        ├── editor/            # TypeScript ソース（Tiptap）
        │   └── src/
        ├── dist/              # ビルド成果物
        ├── css/
        └── js/
```

---

## パフォーマンス目標

| 指標 | 目標値 |
|---|---|
| 画面遷移レスポンス | 200ms 以下 |
| 全文検索 | 50ms 以下（1万ページ規模）|
| 同時接続 | 1,000 ユーザー |
| エディタ入力遅延 | 0ms（ローカル JS 処理）|
| インデックス断片化 | 自動管理（閾値 30%）|
