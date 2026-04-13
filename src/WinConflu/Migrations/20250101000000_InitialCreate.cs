// ============================================================
// WinConflu.NET — EF Core 初回マイグレーション
// HierarchyId + フルテキスト検索インデックス
// ============================================================
// 生成コマンド:
//   dotnet ef migrations add InitialCreate
// 適用コマンド:
//   dotnet ef database update
// ============================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinConflu.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── Pages テーブル ────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Pages",
            columns: table => new
            {
                Id         = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Title      = table.Column<string>(maxLength: 500, nullable: false),
                Content    = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Path       = table.Column<Microsoft.EntityFrameworkCore.HierarchyId>(
                                 type: "hierarchyid", nullable: false),
                AdGroupSid = table.Column<string>(maxLength: 100, nullable: true),
                Slug       = table.Column<string>(maxLength: 300, nullable: true),
                IsDeleted  = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedBy  = table.Column<string>(nullable: false),
                UpdatedBy  = table.Column<string>(nullable: false),
                CreatedAt  = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt  = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_Pages", x => x.Id));

        migrationBuilder.CreateIndex("IX_Pages_Path",
            "Pages", "Path");
        migrationBuilder.CreateIndex("IX_Pages_Path_BreadthFirst",
            "Pages", new[] { "Path", "Id" });

        // ── Revisions テーブル ────────────────────────────
        migrationBuilder.CreateTable(
            name: "Revisions",
            columns: table => new
            {
                Id        = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PageId    = table.Column<int>(nullable: false),
                Content   = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AuthorSid = table.Column<string>(maxLength: 100, nullable: false),
                Comment   = table.Column<string>(nullable: true),
                Version   = table.Column<int>(nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Revisions", x => x.Id);
                t.ForeignKey("FK_Revisions_Pages_PageId",
                    x => x.PageId, "Pages", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_Revisions_PageId", "Revisions", "PageId");
        migrationBuilder.CreateIndex("IX_Revisions_CreatedAt", "Revisions", "CreatedAt");

        // ── Projects テーブル ─────────────────────────────
        migrationBuilder.CreateTable(
            name: "Projects",
            columns: table => new
            {
                Id               = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name             = table.Column<string>(maxLength: 200, nullable: false),
                Prefix           = table.Column<string>(maxLength: 10, nullable: false),
                Description      = table.Column<string>(nullable: true),
                AdGroupOwnerSid  = table.Column<string>(maxLength: 100, nullable: false),
                IsArchived       = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedAt        = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_Projects", x => x.Id));

        migrationBuilder.CreateIndex("IX_Projects_Prefix",
            "Projects", "Prefix", unique: true);

        // ── Issues テーブル ───────────────────────────────
        migrationBuilder.CreateTable(
            name: "Issues",
            columns: table => new
            {
                Id            = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId     = table.Column<int>(nullable: false),
                IssueNumber   = table.Column<int>(nullable: false),
                Title         = table.Column<string>(maxLength: 500, nullable: false),
                Description   = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Status        = table.Column<string>(maxLength: 20, nullable: false),
                Priority      = table.Column<string>(maxLength: 10, nullable: false),
                AssigneeSid   = table.Column<string>(maxLength: 100, nullable: true),
                ReporterSid   = table.Column<string>(maxLength: 100, nullable: true),
                DueDate       = table.Column<DateTimeOffset>(nullable: true),
                IsDeleted     = table.Column<bool>(nullable: false, defaultValue: false),
                OutlookSynced = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedAt     = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt     = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Issues", x => x.Id);
                t.ForeignKey("FK_Issues_Projects_ProjectId",
                    x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_Issues_ProjectId",   "Issues", "ProjectId");
        migrationBuilder.CreateIndex("IX_Issues_Status",      "Issues", "Status");
        migrationBuilder.CreateIndex("IX_Issues_AssigneeSid", "Issues", "AssigneeSid");
        migrationBuilder.CreateIndex("IX_Issues_DueDate",     "Issues", "DueDate");

        // ── Wiki ↔ Issue 中間テーブル ─────────────────────
        migrationBuilder.CreateTable(
            name: "PageIssueLinks",
            columns: table => new
            {
                LinkedIssuesId = table.Column<int>(nullable: false),
                LinkedPagesId  = table.Column<int>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PageIssueLinks", x => new { x.LinkedIssuesId, x.LinkedPagesId });
                t.ForeignKey("FK_PageIssueLinks_Issues_LinkedIssuesId",
                    x => x.LinkedIssuesId, "Issues", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PageIssueLinks_Pages_LinkedPagesId",
                    x => x.LinkedPagesId, "Pages", "Id", onDelete: ReferentialAction.Cascade);
            });

        // ── Attachments / AuditLogs / Comments ───────────
        migrationBuilder.CreateTable("Attachments",
            columns: table => new
            {
                Id          = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                RelatedType = table.Column<string>(nullable: false),
                RelatedId   = table.Column<int>(nullable: false),
                FileName    = table.Column<string>(maxLength: 500, nullable: false),
                BlobUrl     = table.Column<string>(maxLength: 2000, nullable: false),
                ContentType = table.Column<string>(maxLength: 200, nullable: true),
                SizeBytes   = table.Column<long>(nullable: false),
                UploadedBy  = table.Column<string>(nullable: false),
                UploadedAt  = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_Attachments", x => x.Id));
        migrationBuilder.CreateIndex("IX_Attachments_RelatedId", "Attachments", "RelatedId");

        migrationBuilder.CreateTable("AuditLogs",
            columns: table => new
            {
                Id         = table.Column<long>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UserSid    = table.Column<string>(maxLength: 100, nullable: false),
                Action     = table.Column<string>(maxLength: 50, nullable: false),
                EntityType = table.Column<string>(maxLength: 100, nullable: false),
                EntityId   = table.Column<int>(nullable: true),
                OldValue   = table.Column<string>(nullable: true),
                NewValue   = table.Column<string>(nullable: true),
                IpAddress  = table.Column<string>(nullable: true),
                CreatedAt  = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_AuditLogs", x => x.Id));
        migrationBuilder.CreateIndex("IX_AuditLogs_CreatedAt", "AuditLogs", "CreatedAt");
        migrationBuilder.CreateIndex("IX_AuditLogs_UserSid",   "AuditLogs", "UserSid");

        migrationBuilder.CreateTable("Comments",
            columns: table => new
            {
                Id          = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                RelatedType = table.Column<string>(nullable: false),
                RelatedId   = table.Column<int>(nullable: false),
                Body        = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AuthorSid   = table.Column<string>(maxLength: 100, nullable: false),
                IsDeleted   = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedAt   = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt   = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_Comments", x => x.Id));
        migrationBuilder.CreateIndex("IX_Comments_RelatedId", "Comments", "RelatedId");

        // ── SQL Server フルテキスト検索インデックス ──────
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'wcn_ftcat'
            )
            BEGIN
                CREATE FULLTEXT CATALOG wcn_ftcat AS DEFAULT;
            END
        ");

        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.fulltext_indexes fi
                JOIN sys.tables t ON fi.object_id = t.object_id
                WHERE t.name = 'Pages'
            )
            BEGIN
                CREATE FULLTEXT INDEX ON Pages(Title, Content)
                KEY INDEX PK_Pages
                ON wcn_ftcat
                WITH CHANGE_TRACKING AUTO;
            END
        ");

        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.fulltext_indexes fi
                JOIN sys.tables t ON fi.object_id = t.object_id
                WHERE t.name = 'Issues'
            )
            BEGIN
                CREATE FULLTEXT INDEX ON Issues(Title, Description)
                KEY INDEX PK_Issues
                ON wcn_ftcat
                WITH CHANGE_TRACKING AUTO;
            END
        ");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'wcn_ftcat') DROP FULLTEXT CATALOG wcn_ftcat;");

        migrationBuilder.DropTable("PageIssueLinks");
        migrationBuilder.DropTable("Comments");
        migrationBuilder.DropTable("AuditLogs");
        migrationBuilder.DropTable("Attachments");
        migrationBuilder.DropTable("IssueLabels");
        migrationBuilder.DropTable("Issues");
        migrationBuilder.DropTable("Projects");
        migrationBuilder.DropTable("Revisions");
        migrationBuilder.DropTable("Pages");
    }
}
