// ============================================================
// WinConflu.NET — マイグレーション: ワークフロー / IssueType / エピック階層
// ============================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinConflu.Migrations;

public partial class AddWorkflowAndIssueHierarchy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── Issues テーブルに列追加 ───────────────────────────
        migrationBuilder.AddColumn<string>(
            name:         "Type",
            table:        "Issues",
            type:         "nvarchar(20)",
            nullable:     false,
            defaultValue: "Task");

        migrationBuilder.AddColumn<int>(
            name:     "ParentIssueId",
            table:    "Issues",
            nullable: true);

        migrationBuilder.AddForeignKey(
            name:       "FK_Issues_Issues_ParentIssueId",
            table:      "Issues",
            column:     "ParentIssueId",
            principalTable:  "Issues",
            principalColumn: "Id",
            onDelete:   ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            "IX_Issues_ParentIssueId", "Issues", "ParentIssueId");

        // ── WorkflowDefinitions ───────────────────────────────
        migrationBuilder.CreateTable(
            name: "WorkflowDefinitions",
            columns: table => new
            {
                Id        = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<int>(nullable: false),
                Name      = table.Column<string>(maxLength: 200, nullable: false),
                IsDefault = table.Column<bool>(nullable: false, defaultValue: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                t.ForeignKey("FK_WorkflowDefinitions_Projects_ProjectId",
                    x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            "IX_WorkflowDefinitions_ProjectId_IsDefault",
            "WorkflowDefinitions", new[] { "ProjectId", "IsDefault" });

        // ── WorkflowTransitions ───────────────────────────────
        migrationBuilder.CreateTable(
            name: "WorkflowTransitions",
            columns: table => new
            {
                Id             = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                WorkflowId     = table.Column<int>(nullable: false),
                FromStatus     = table.Column<string>(maxLength: 20, nullable: false),
                ToStatus       = table.Column<string>(maxLength: 20, nullable: false),
                RequiredRole   = table.Column<string>(nullable: true),
                RequireComment = table.Column<bool>(nullable: false, defaultValue: false),
                AutomationJson = table.Column<string>(nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_WorkflowTransitions", x => x.Id);
                t.ForeignKey("FK_WorkflowTransitions_WorkflowDefinitions_WorkflowId",
                    x => x.WorkflowId, "WorkflowDefinitions", "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            "IX_WorkflowTransitions_WorkflowId",
            "WorkflowTransitions", "WorkflowId");

        // ── IssueDependencies ─────────────────────────────────
        migrationBuilder.CreateTable(
            name: "IssueDependencies",
            columns: table => new
            {
                Id            = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SourceIssueId = table.Column<int>(nullable: false),
                TargetIssueId = table.Column<int>(nullable: false),
                Type          = table.Column<string>(maxLength: 20, nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_IssueDependencies", x => x.Id);
                t.ForeignKey("FK_IssueDependencies_Issues_SourceIssueId",
                    x => x.SourceIssueId, "Issues", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_IssueDependencies_Issues_TargetIssueId",
                    x => x.TargetIssueId, "Issues", "Id",
                    onDelete: ReferentialAction.NoAction); // 循環を避けて NoAction
            });

        migrationBuilder.CreateIndex(
            "IX_IssueDependencies_SourceIssueId", "IssueDependencies", "SourceIssueId");
        migrationBuilder.CreateIndex(
            "IX_IssueDependencies_TargetIssueId", "IssueDependencies", "TargetIssueId");

        // ── 標準ワークフローのシードデータ ──────────────────
        // プロジェクトがない状態でのグローバルテンプレートは別途実装
        // ここでは SQL 直書きではなく、ApplicationBuilderの EnsureDefaultWorkflowAsync で対応
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("IssueDependencies");
        migrationBuilder.DropTable("WorkflowTransitions");
        migrationBuilder.DropTable("WorkflowDefinitions");
        migrationBuilder.DropColumn("ParentIssueId", "Issues");
        migrationBuilder.DropColumn("Type", "Issues");
    }
}
