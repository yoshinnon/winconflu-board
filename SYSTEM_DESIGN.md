# WinConflu.NET + Boards システム設計書

**文書バージョン:** 1.0  
**作成日:** 2026年4月  
**対象システム:** WinConflu.NET + Boards  
**想定読者:** アーキテクト、バックエンドエンジニア、フロントエンドエンジニア、インフラエンジニア

---

## 目次

1. システム概要
2. アーキテクチャ概要
3. 技術スタック
4. データモデル設計
5. サービス層設計
6. フロントエンド設計（Blazor + Tiptap）
7. 認証・認可設計
8. M365 インテグレーション設計
9. インフラ構成（Azure / Terraform）
10. CI/CD パイプライン設計
11. パフォーマンス設計
12. セキュリティ設計
13. データフロー図
14. 運用設計
15. ファイル・ディレクトリ構造

---

## 1. システム概要

### 1.1 目的

WinConflu.NET + Boards は、Atlassian Confluence + Jira の主要機能を Azure / Windows 環境に特化した形で再実装した、エンタープライズ向けナレッジ管理・タスク管理プラットフォームである。

### 1.2 対象規模

- 想定ユーザー数: 最大 1,000 名（同時接続 200 名）
- Wiki ページ数: 数万件規模
- チケット数: 数十万件規模

### 1.3 主要機能

| カテゴリ | 機能 |
|---|---|
| Wiki（Confluence 相当） | 無限階層ページ・WYSIWYG エディタ（Tiptap）・テンプレート・インラインコメント・全文検索・バージョン管理・差分表示 |
| Boards（Jira 相当） | カンバンボード・IssueType 管理・エピック階層・ワークフロー管理・ロードマップ（ガントチャート）・チケット依存関係 |
| M365 連携 | Teams プレゼンス・Outlook 予定表同期・AD ユーザー検索・@メンション |
| 通知 | アプリ内通知・期限リマインダー・期限超過通知（Hangfire） |

---

## 2. アーキテクチャ概要

### 2.1 全体構成図

```
┌─────────────────────────────────────────────────────────────────┐
│ Azure App Service (Windows / P2v3)                              │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Blazor Server (.NET 10)                                  │   │
│  │                                                         │   │
│  │  Browser ──SignalR──► Blazor Server ──► AppDbContext    │   │
│  │     │                      │                │           │   │
│  │     │ JS Interop            │ DI            ▼           │   │
│  │  Tiptap                 Services    EF Core 10          │   │
│  │  (WYSIWYG)              Layer                           │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌──────────────┐  ┌────────────────┐  ┌────────────────────┐  │
│  │ Hangfire     │  │ Application    │  │ Key Vault          │  │
│  │ (SQL Server) │  │ Insights       │  │ (シークレット管理) │  │
│  └──────────────┘  └────────────────┘  └────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
         │                    │                    │
         ▼                    ▼                    ▼
┌─────────────────┐  ┌────────────────┐  ┌───────────────────┐
│ Azure SQL DB    │  │ Azure Blob     │  │ Azure SignalR     │
│ (EF Core 10)    │  │ Storage        │  │ Service           │
│ HierarchyId 対応│  │ (添付ファイル) │  │ (1000接続スケール)│
└─────────────────┘  └────────────────┘  └───────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────┐
│ Microsoft Graph API v5.x                                        │
│ (Teams プレゼンス / Outlook / AD ユーザー検索)                  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 アーキテクチャパターン

- **Blazor Server モデル**: UI のレンダリングをサーバーサイドで処理。SignalR 経由でブラウザと通信。
- **ハイブリッドエディタ**: Wiki エディタのみ Tiptap（TypeScript）をクライアント側で動作させ、DOM 管理権を JS に委譲。C# 側は JSON の保存・変換のみを担当。
- **サービス層分離**: 全ドメインロジックを `IXxxService` インターフェース経由で DI。テスト容易性と疎結合を確保。
- **マルチテナント対応の基盤**: AD グループ単位のアクセス制御を `Page.AdGroupSid` と `IAdGroupService` で実現。

---

## 3. 技術スタック

### 3.1 バックエンド

| 技術 | バージョン | 用途 |
|---|---|---|
| .NET / ASP.NET Core | 10 (LTS) | アプリケーションフレームワーク |
| Blazor Server | 10 | UI フレームワーク（サーバーサイドレンダリング）|
| EF Core | 10 | ORM |
| Microsoft.EntityFrameworkCore.SqlServer.HierarchyId | 6.x | 階層型ページ構造 |
| Markdig | 0.x | Markdown → HTML レンダリング（閲覧用）|
| DiffPlex | 1.x | バージョン差分表示 |
| Hangfire | 1.x | バックグラウンドジョブ（通知エンジン）|
| Microsoft.Graph | 5.x | M365 API 連携 |
| Azure.Identity | 1.x | Managed Identity 認証 |
| Azure.Storage.Blobs | 12.x | ファイル添付 |
| Microsoft.Azure.SignalR | 1.x | SignalR スケールアウト |

### 3.2 フロントエンド

| 技術 | バージョン | 用途 |
|---|---|---|
| MudBlazor | 9.x | UI コンポーネントライブラリ |
| Tiptap | 2.11.x | WYSIWYG エディタ本体 |
| TypeScript | 5.8.x | エディタ実装言語 |
| Vite | 6.x | TypeScript ビルドツール（IIFE バンドル）|
| lowlight | 3.x | コードブロックのシンタックスハイライト |
| tippy.js | 6.x | スラッシュパレット・@メンションポップアップ |

### 3.3 インフラ

| 技術 | 用途 |
|---|---|
| Terraform (azurerm ~> 4.0) | IaC（インフラ定義）|
| GitHub Actions (OIDC) | CI/CD パイプライン |
| Azure App Service (Windows P2v3) | アプリケーションホスティング |
| Azure SQL Database | プライマリデータストア |
| Azure Blob Storage | 添付ファイルストア |
| Azure Key Vault | シークレット管理 |
| Azure SignalR Service | WebSocket スケールアウト |
| Application Insights | 監視・ログ |

---

## 4. データモデル設計

### 4.1 主要エンティティ関係

```
Project (1) ──────────── (N) Issue
                                │
                    ┌───────────┼───────────┐
                    │           │           │
               SubIssues    Labels      Dependencies
               (自己参照)

Page (HierarchyId)
  │
  ├── Revisions          (バージョン履歴)
  ├── Attachments        (添付ファイル)
  ├── InlineAnnotations  (インラインコメント)
  │     └── AnnotationReplies
  └── LinkedIssues ←──── (多対多) ─────► Issue.LinkedPages

Project ── WorkflowDefinition ── WorkflowTransition
```

### 4.2 Page エンティティ（コアモデル）

| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | |
| Title | nvarchar(500) | ページタイトル |
| Content | nvarchar(max) | コンテンツ本体（JSON or Markdown）|
| ContentFormat | nvarchar(20) | "json" (Tiptap) / "markdown" (既存) |
| ContentText | nvarchar(max) | FTS 用プレーンテキストキャッシュ |
| Path | HierarchyId | 階層パス（例: /1/3/2/）|
| AdGroupSid | nvarchar(100) | アクセス制御 AD グループ SID |
| Slug | nvarchar(200) | URL フレンドリー識別子 |
| IsDeleted | bit | 論理削除フラグ |
| CreatedBy / UpdatedBy | nvarchar(100) | 操作者 AD アカウント |
| CreatedAt / UpdatedAt | datetimeoffset | タイムスタンプ |

### 4.3 Issue エンティティ

| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | |
| ProjectId | int (FK) | プロジェクト |
| IssueNumber | int | プロジェクト内連番（WCN-12）|
| Type | nvarchar(20) | Task/Bug/Story/Epic/SubTask |
| Status | nvarchar(20) | Todo/Doing/Done/Verified |
| Priority | nvarchar(10) | Low/Medium/High/Critical |
| ParentIssueId | int (FK nullable) | エピック階層 |
| StartDate | datetimeoffset? | ロードマップ開始日 |
| DueDate | datetimeoffset? | 期限 |
| StoryPoints | int? | スクラム見積もり |
| Progress | int (0-100) | 完了率 |
| OutlookSynced | bit | Outlook 同期済みフラグ |

### 4.4 インデックス設計

```sql
-- 階層クエリ用
CREATE INDEX IX_Pages_Path ON Pages (Path);

-- FTS（CONTAINSTABLE）用
CREATE FULLTEXT CATALOG wcn_ftcat AS DEFAULT;
CREATE FULLTEXT INDEX ON Pages(Title, Content, ContentText)
    KEY INDEX PK_Pages;
CREATE FULLTEXT INDEX ON Issues(Title, Description)
    KEY INDEX PK_Issues;

-- カンバンビュー用
CREATE INDEX IX_Issues_ProjectId_Status ON Issues (ProjectId, Status);
CREATE INDEX IX_Issues_AssigneeSid ON Issues (AssigneeSid);
CREATE INDEX IX_Issues_DueDate ON Issues (DueDate);

-- 通知ポーリング用
CREATE INDEX IX_AppNotifications_RecipientSid_IsRead
    ON AppNotifications (RecipientSid, IsRead);
```

---

## 5. サービス層設計

### 5.1 サービス一覧

| サービス | インターフェース | 役割 |
|---|---|---|
| WikiService | IWikiService | ページ CRUD・HierarchyId 操作・ロールバック |
| BoardService | IBoardService | チケット CRUD・ワークフロー統合・Outlook 同期 |
| WorkflowService | IWorkflowService | 遷移バリデーション・オートメーション実行 |
| TemplateService | ITemplateService | テンプレート管理・変数展開・シード |
| InlineAnnotationService | IInlineAnnotationService | インラインコメント・アンカー再計算 |
| MarkdownService | IMarkdownService | Markdown → HTML 変換（閲覧専用）|
| ProseMirrorService | IProseMirrorService | JSON ↔ HTML/Markdown/PlainText 変換 |
| DiffService | IDiffService | DiffPlex ラッパー（インライン/サイドバイサイド）|
| SearchService | ISearchService | FTS 横断検索・スニペット生成 |
| NotificationService | INotificationService | アプリ内通知・Hangfire ジョブ登録 |
| CacheService | ICacheService | 多段キャッシュ・一括無効化 |
| PerformanceService | IPerformanceService | DMV 分析・インデックス再構築 |
| TeamsPresenceService | IPresenceService | Teams プレゼンス（バッチ取得）|
| OutlookCalendarService | ICalendarService | Outlook 予定表 Upsert |
| UserProfileService | IUserProfileService | AD ユーザー検索・顔写真 |
| AttachmentService | IAttachmentService | Blob Storage CRUD |
| AuditService | IAuditService | 操作ログ記録 |
| AdGroupService | IAdGroupService | AD グループ取得・ロール検証 |

### 5.2 WikiService の主要ロジック

**HierarchyId による子ページ取得:**

```csharp
// 直接の子のみ（1レベル）
db.Pages.Where(p => p.Path.GetAncestor(1) == parentPath)

// サブツリー全取得
db.Pages.Where(p => p.Path.IsDescendantOf(targetPath))

// 次の兄弟パスの計算
parentPath.GetDescendant(lastSiblingPath, null)
```

**コンテンツ保存フロー（WYSIWYG）:**

```
1. Tiptap → JSON (JS)
2. JSInterop → C# JsonElement
3. ProseMirrorService.ToPlainText() → ContentText (FTS 用)
4. SaveChanges() → Azure SQL
5. AnnotationService.RecalculateAnchorsAsync() → アンカー検証
```

### 5.3 WorkflowService のオートメーション

```
MoveIssueAsync()
    │
    ├── ValidateTransitionAsync()
    │       ├── ワークフロー定義なし → 自由遷移
    │       ├── FromStatus → ToStatus の遷移定義を検索
    │       ├── RequiredRole があれば AdGroupService で検証
    │       └── RequireComment があればコメント必須チェック
    │
    └── RunAutomationsAsync()
            ├── AllSubTasksDone → 親チケット自動 Done
            ├── DueDateApproaching → NotificationService で通知
            └── AssignToReporter → 担当者自動設定
```

---

## 6. フロントエンド設計（Blazor + Tiptap）

### 6.1 エディタアーキテクチャ（ハイブリッド設計）

```
┌─────────────────────────────────────────────────────────────┐
│ WikiPage.razor                                              │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ TiptapEditorComponent.razor                           │  │
│  │  ┌─────────────────┐      ┌─────────────────────────┐ │  │
│  │  │ Blazor Toolbar   │      │ DOM Container            │ │  │
│  │  │ (C# rendered)   │      │ ShouldRender() = false  │ │  │
│  │  │                 │      │                         │ │  │
│  │  │ ExecCmd('bold') │      │  ┌──────────────────┐   │ │  │
│  │  │       │         │      │  │  Tiptap Editor   │   │ │  │
│  │  │       ▼         │      │  │  (TypeScript)    │   │ │  │
│  │  │  JSInterop      │─────►│  │  DOM を完全管理  │   │ │  │
│  │  │  wcnEditorCmds  │      │  └──────────────────┘   │ │  │
│  │  │  .exec()        │      └─────────────────────────┘ │  │
│  │  └─────────────────┘                                  │  │
│  │                                                       │  │
│  │  [JSInvokable] OnEditorChange(json)  ◄── Tiptap       │  │
│  │  [JSInvokable] SearchMentionUsers()  ◄── @メンション  │  │
│  │  [JSInvokable] GetIssueBadge()       ◄── [[KEY-NNN]]  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**DOM 管理の競合回避:**  
`ShouldRender() => false` により Blazor の差分レンダリングエンジンが TiptapEditorComponent の DOM に触れない。エディタの mount/unmount は `OnAfterRenderAsync(firstRender: true)` と `DisposeAsync()` で完全に制御。

### 6.2 Tiptap 拡張一覧

| 拡張 | 種別 | 機能 |
|---|---|---|
| StarterKit | Built-in | 基本テキスト書式・段落・リスト・履歴 |
| Table / TableRow / TableCell / TableHeader | Built-in | 表編集（列リサイズ・セル結合）|
| TaskList / TaskItem | Built-in | チェックボックス付きリスト |
| Mention | Built-in | @メンション（Graph API 連携）|
| CodeBlockLowlight | Built-in | シンタックスハイライト付きコードブロック |
| InfoBox | Custom Node | Confluence パネルマクロ（info/warning/tip/danger）|
| IssueEmbed | Custom Node | Boards チケット埋め込み（`[[WCN-12]]`）|
| SlashCommand | Custom Plugin | `/` スラッシュコマンドパレット |
| IssuePattern | Custom Plugin | `[[KEY-NNN]]` テキスト自動展開 |

### 6.3 データフロー（保存）

```
[編集中]  Tiptap JSON (インメモリ)
    │
    │ onUpdate (デバウンス 300ms)
    ▼
[JS] invokeMethodAsync('OnEditorChange', json)
    │
    │ [C#] _currentJson = json
    │
[保存ボタン]
    │
    │ wcnEditor.getJson(editorId) → 最新 JSON
    ▼
[C#] ProseMirrorService.ToPlainText(json) → ContentText
    │
    ▼
[DB] UPDATE Pages SET Content=json, ContentFormat='json', ContentText=... 
```

### 6.4 ビルドパイプライン（TypeScript）

```
wwwroot/editor/src/
  ├── editor.ts        (エントリポイント・公開 API)
  ├── extensions.ts    (カスタム拡張)
  ├── commands.ts      (ツールバーコマンドディスパッチャ)
  └── types.ts         (型定義)
        │
        │ vite build (IIFE 形式)
        ▼
wwwroot/dist/
  └── wcn-editor.iife.js  → window.wcnEditor として公開
```

`WinConflu.csproj` の `<Target Name="BuildTiptap">` により `dotnet build` 時に自動実行。

---

## 7. 認証・認可設計

### 7.1 認証フロー

```
Browser → IIS / Kestrel (Windows 認証 / Negotiate)
              │
              │ Kerberos / NTLM
              ▼
         ClaimsPrincipal
              │
              ├── Identity.Name → AD アカウント名（UPN）
              └── GroupSid claims → AD グループ一覧
```

ゼロコンフィグ SSO：AD 参加 PC からアクセスすると認証ダイアログなしでログイン完了。

### 7.2 認可モデル

| レベル | 実装 | 説明 |
|---|---|---|
| アプリケーション全体 | `FallbackPolicy = DefaultPolicy` | 未認証ユーザーは全拒否 |
| ページアクセス | `Page.AdGroupSid` | 特定 AD グループのみ閲覧可 |
| ワークフロー遷移 | `WorkflowTransition.RequiredRole` | ステータス変更に AD グループロール必須 |
| Hangfire ダッシュボード | `HangfireAuthorizationFilter` | 認証済みユーザーのみ |

### 7.3 Azure Managed Identity

アプリケーションは `DefaultAzureCredential` で以下のリソースにパスワードレスでアクセス：
- Azure SQL Database（Managed Identity ユーザー）
- Azure Blob Storage（Storage Blob Data Contributor）
- Azure Key Vault（Key Vault Secrets User）
- Microsoft Graph API（アプリケーション権限）

---

## 8. M365 インテグレーション設計

### 8.1 Graph API 権限

| 権限 | 種別 | 用途 |
|---|---|---|
| `Presence.Read.All` | Application | Teams プレゼンス一括取得 |
| `User.Read.All` | Application | AD ユーザー検索・プロファイル |
| `Calendars.ReadWrite` | Application | Outlook 予定表 Upsert |

### 8.2 プレゼンス取得の最適化

```
GetBulkPresenceAsync(userIds)
    │
    ├── キャッシュヒット分を除外（TTL: 30秒）
    │
    └── graph.Communications.GetPresencesByUserId.PostAsGetPresencesByUserIdPostResponseAsync()
            └── 最大 650 ユーザーを 1 リクエストで取得
```

### 8.3 Outlook 予定表連携

チケットの識別は `SingleValueExtendedProperty`（MAPI 拡張属性）を使用：

```
Property ID: String {00020329-...} Name com.winconflu.issueId
Value: "wcn-issue-{issueId}"
```

これにより Outlook 予定表上での重複作成を防ぎ、Upsert（作成・更新・削除）が可能。

---

## 9. インフラ構成（Azure / Terraform）

### 9.1 リソース構成

| リソース | SKU / 構成 | 用途 |
|---|---|---|
| App Service Plan | P2v3 (2コア/8GB, Windows) | アプリホスティング |
| App Service | .NET 10, Slot: staging | アプリケーション本体 |
| Azure SQL Database | S3 (100 DTU) + SQL Server 2025 ポリシー | プライマリ DB |
| Azure Blob Storage | Standard LRS | 添付ファイル |
| Azure Key Vault | Standard | シークレット管理 |
| Azure SignalR Service | Standard S1 (1ユニット) | WebSocket スケールアウト |
| Application Insights | 従量課金 | 監視・ログ |

### 9.2 Terraform 構成

```hcl
# State 管理: Azure Blob Storage バックエンド
terraform {
  backend "azurerm" {
    resource_group_name  = "wcn-tfstate-rg"
    storage_account_name = "wcntfstateprod"
    container_name       = "tfstate"
    key                  = "prod/terraform.tfstate"
  }
}
```

### 9.3 Hotpatch / パッチ管理

Azure Update Manager + Hotpatch 対応イメージを使用し、OS パッチの再起動を年4回に抑制（Hotpatch 対応月）。アプリ更新は Deployment Slot Swap でゼロダウンタイム実現。

---

## 10. CI/CD パイプライン設計

### 10.1 デプロイパイプライン（deploy.yml）

```
push to main
    │
    ▼
1. Build & Test
   ├── dotnet restore
   ├── npm install && npm run build  (Tiptap ビルド)
   ├── dotnet build --configuration Release
   └── dotnet test
    │
    ▼
2. EF Core Migration
   └── dotnet ef database update --connection ${{ secrets.SQL_CONNECTION_STRING }}
    │
    ▼
3. Deploy to Staging Slot
   └── az webapp deploy --slot staging
    │
    ▼
4. Health Check (staging)
   └── curl https://wcn-prod.azurewebsites.net/health → 200 OK
    │
    ▼
5. Slot Swap (staging → production)
   └── az webapp deployment slot swap
    │
    ▼
6. Health Check (production)
   └── 失敗時: az webapp deployment slot swap --action rollback
```

### 10.2 認証（OIDC）

Federated Identity Credential を使用し、GitHub Actions から Azure へのサービスプリンシパルキーレス認証を実現。シークレットの有効期限切れリスクを排除。

---

## 11. パフォーマンス設計

### 11.1 多段キャッシュ

| 階層 | TTL | 対象データ |
|---|---|---|
| Volatile | 30秒 | Teams プレゼンス |
| Short | 2分 | チケット一覧 |
| Medium | 5分 | Wiki ページツリー |
| Stable | 30分 | AD グループ情報 |
| Long | 6時間 | ユーザー顔写真 |

### 11.2 DB パフォーマンス最適化

- `QueryTrackingBehavior.NoTrackingWithIdentityResolution` をデフォルト設定
- HierarchyId の `IsDescendantOf` / `GetAncestor` によるインデックスフレンドリーな階層クエリ
- CONTAINSTABLE による FTS（フルテキストサーチ）で LIKE 検索を廃止
- Hangfire ジョブ（毎週日曜 2:00）でインデックス断片化を自動再構築

### 11.3 SignalR スケールアウト

Azure SignalR Service を使用し、複数インスタンス間でのリアルタイム通信（通知プッシュ）を実現。Blazor Server の接続は SignalR サービスにバックプレインを委譲。

---

## 12. セキュリティ設計

### 12.1 シークレット管理

| シークレット | 保管場所 |
|---|---|
| SQL 接続文字列 | Azure Key Vault |
| Blob Storage 接続情報 | Managed Identity（接続文字列不要）|
| Graph API クライアント情報 | Azure Key Vault |
| SignalR 接続文字列 | Azure Key Vault |

### 12.2 入力バリデーション

- Markdig の `DisableHtml()` により HTML インジェクションを防止
- ProseMirrorService では `HtmlEncode` を適用してから HTML 生成
- リンク URL は `HtmlAttributeEncode` でエスケープ
- SQL インジェクション: EF Core のパラメータクエリで完全防止

### 12.3 監査ログ

全 Create / Update / Delete / Move 操作を `AuditLog` テーブルに記録：

```
UserSid | Action | EntityType | EntityId | OldValue | NewValue | IpAddress | CreatedAt
```

---

## 13. データフロー図

### 13.1 Wiki ページ保存（WYSIWYG フロー）

```
User Input (Tiptap)
    │ onUpdate (debounce 300ms)
    ▼
JS: editor.getJSON() → ProseMirror JSON
    │ invokeMethodAsync('OnEditorChange', json)
    ▼
C#: TiptapEditorComponent._currentJson = json
    │ [保存ボタン]
    ▼
C#: _editor.GetJsonAsync() → 確定 JSON
    │
    ├── ProseMirrorService.ToPlainText() → ContentText
    │
    ▼
WikiService.UpdatePageAsync(
    title, json, "json", slug, null, comment)
    │
    ├── Page.Content = json
    ├── Page.ContentFormat = "json"
    ├── Page.ContentText = plainText
    ├── Revision 追記
    └── SaveChangesAsync()
    │
    ▼
AnnotationService.RecalculateAnchorsAsync(pageId, plainText)
    └── Open 注釈のオフセットを plainText で再検証
```

### 13.2 チケット通知フロー

```
Hangfire (毎時 / 毎朝 9:00)
    │
    ▼
NotificationService.CheckDueDateReminderJob()
    │
    ├── db.Issues WHERE DueDate <= NOW+24h AND Status != Done
    │
    └── NotifyAsync(assigneeSid, title, body, kind, linkUrl)
            │
            ▼
        db.AppNotifications INSERT
            │
            ▼ (SignalR Hub 経由でリアルタイム通知も可)
        NotificationBell.razor (30秒ポーリング)
```

---

## 14. 運用設計

### 14.1 ヘルスチェック

`GET /health` で以下を確認：
- Azure SQL Database への接続
- Azure Blob Storage への接続

GitHub Actions の Slot Swap 前後に自動実行。失敗時は自動ロールバック。

### 14.2 定期メンテナンスジョブ（Hangfire）

| ジョブ ID | スケジュール | 処理内容 |
|---|---|---|
| due-date-reminder | 毎時 0 分 | 24時間以内期限チケットに通知 |
| overdue-check | 毎朝 9:00 | 期限超過チケットに通知 |
| index-rebuild-weekly | 毎週日曜 2:00 | DB インデックス断片化解消 |

### 14.3 ログ・監視

- Application Insights でリクエストレイテンシ・例外・依存関係を自動収集
- Hangfire ダッシュボード（`/hangfire`）でジョブ実行履歴を確認
- AuditLog テーブルで全操作の証跡を保持

---

## 15. ファイル・ディレクトリ構造

```
winconflu/
├── README.md                              # セットアップガイド
├── SYSTEM_DESIGN.md                       # 本設計書
├── .github/
│   └── workflows/
│       ├── deploy.yml                     # アプリデプロイパイプライン
│       └── terraform.yml                  # インフラ管理パイプライン
├── terraform/
│   ├── main.tf                            # リソース定義（App Service / SQL / Blob 等）
│   ├── variables.tf                       # 変数定義
│   ├── outputs.tf                         # 出力値
│   ├── backend.tf                         # State バックエンド設定
│   └── environments/
│       ├── prod/
│       │   ├── terraform.tfvars           # 本番環境変数
│       │   └── backend.hcl               # 本番 State 設定
│       └── dev/
│           └── terraform.tfvars           # 開発環境変数
└── src/
    └── WinConflu/
        ├── WinConflu.csproj               # プロジェクト定義（npm ビルド統合含む）
        ├── Program.cs                     # DI 登録・ミドルウェア設定・起動処理
        ├── appsettings.json               # 設定ファイル（Key Vault 参照含む）
        ├── appsettings.Development.json   # 開発用設定
        ├── Components/
        │   ├── App.razor                  # ルートコンポーネント（HTML ヘッド定義）
        │   ├── Routes.razor               # Blazor ルーター（認証ガード）
        │   ├── _Imports.razor             # グローバル using 宣言
        │   ├── Layout/
        │   │   └── MainLayout.razor       # 共通レイアウト（トップバー・サイドナビ）
        │   ├── Pages/
        │   │   ├── WikiPage.razor         # Wiki 閲覧・編集（Tiptap 統合）
        │   │   ├── KanbanBoard.razor      # カンバン・エピックビュー
        │   │   ├── RoadmapPage.razor      # ガントチャート（SVG）
        │   │   ├── SearchPage.razor       # 全文検索結果
        │   │   └── SettingsPage.razor     # 管理設定（DB 監視含む）
        │   └── Shared/
        │       ├── TiptapEditorComponent.razor    # Tiptap ラッパー（JSInterop）
        │       ├── InlineAnnotationPanel.razor    # インラインコメントパネル
        │       ├── NotificationBell.razor         # 通知ベル
        │       ├── RevisionDiffDialog.razor       # 差分表示ダイアログ
        │       ├── TemplatePickerDialog.razor     # テンプレート選択
        │       └── UserSearchPopover.razor        # AD ユーザー検索
        ├── Data/
        │   └── AppDbContext.cs            # EF Core DbContext（全テーブル定義）
        ├── Migrations/
        │   ├── 20250101_InitialCreate.cs
        │   ├── 20250201_AddTemplatesAndAnnotations.cs
        │   ├── 20250301_AddWorkflowAndIssueHierarchy.cs
        │   ├── 20250401_AddNotifications.cs
        │   ├── 20250501_AddIssueExtendedFields.cs
        │   └── 20250601_AddPageContentFormat.cs
        ├── Models/
        │   ├── Entities.cs                # Page / Issue / Revision 等コアエンティティ
        │   ├── BoardsExtended.cs          # IssueType / WorkflowDefinition 等
        │   └── TemplateAndAnnotation.cs   # PageTemplate / InlineAnnotation 等
        ├── Services/
        │   ├── WikiService.cs             # ページ CRUD + HierarchyId 操作
        │   ├── BoardService.cs            # チケット CRUD + ワークフロー統合
        │   ├── WorkflowService.cs         # 遷移バリデーション + オートメーション
        │   ├── TemplateService.cs         # テンプレート管理 + 5種シード
        │   ├── InlineAnnotationService.cs # インラインコメント + アンカー再計算
        │   ├── MarkdownService.cs         # Markdig ラッパー（閲覧用 HTML 生成）
        │   ├── ProseMirrorService.cs      # JSON ↔ HTML/Markdown/PlainText 変換
        │   ├── DiffService.cs             # DiffPlex ラッパー
        │   ├── SearchService.cs           # CONTAINSTABLE 横断検索
        │   ├── NotificationService.cs     # 通知 + Hangfire ジョブ
        │   ├── CacheService.cs            # 多段キャッシュ戦略
        │   ├── PerformanceService.cs      # DMV 分析 + インデックス再構築
        │   ├── SupportingServices.cs      # Audit / AdGroup / BlobAttachment
        │   └── Graph/
        │       └── GraphServices.cs       # Teams 存在 / Outlook / ユーザー検索
        └── wwwroot/
            ├── css/
            │   ├── wcn.css                # Wiki 本文・アノテーション・検索 CSS
            │   └── wcn-editor.css         # Tiptap エディタ UI CSS
            ├── js/
            │   ├── wcn.js                 # Blazor JSInterop ヘルパー
            │   └── wcn-editor-bridge.js   # 貼り付け・後処理ブリッジ
            ├── editor/                    # TypeScript ソース（npm ワークスペース）
            │   ├── package.json
            │   ├── vite.config.ts
            │   └── src/
            │       ├── editor.ts          # Tiptap 初期化・公開 API
            │       ├── extensions.ts      # InfoBox / IssueEmbed / Slash / IssuePattern
            │       ├── commands.ts        # ツールバーコマンドディスパッチャ
            │       └── types.ts           # TypeScript 型定義
            └── dist/                      # ビルド成果物（git 管理外推奨）
                └── wcn-editor.iife.js     # Vite ビルド済みバンドル
```
