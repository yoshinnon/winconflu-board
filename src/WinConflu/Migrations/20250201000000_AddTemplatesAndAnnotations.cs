// ============================================================
// WinConflu.NET — マイグレーション: テンプレート & インラインコメント
// ============================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinConflu.Migrations;

public partial class AddTemplatesAndAnnotations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── PageTemplates ─────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "PageTemplates",
            columns: table => new
            {
                Id            = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name          = table.Column<string>(maxLength: 100, nullable: false),
                Description   = table.Column<string>(nullable: false, defaultValue: ""),
                Category      = table.Column<string>(maxLength: 50, nullable: false, defaultValue: ""),
                IconName      = table.Column<string>(maxLength: 100, nullable: false, defaultValue: "Description"),
                Content       = table.Column<string>(type: "nvarchar(max)", nullable: false),
                VariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                IsBuiltIn     = table.Column<bool>(nullable: false, defaultValue: false),
                IsDeleted     = table.Column<bool>(nullable: false, defaultValue: false),
                UsageCount    = table.Column<int>(nullable: false, defaultValue: 0),
                CreatedBy     = table.Column<string>(nullable: false, defaultValue: ""),
                CreatedAt     = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt     = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_PageTemplates", x => x.Id));

        migrationBuilder.CreateIndex(
            "IX_PageTemplates_Category", "PageTemplates", "Category");

        // ── InlineAnnotations ─────────────────────────────────
        migrationBuilder.CreateTable(
            name: "InlineAnnotations",
            columns: table => new
            {
                Id           = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PageId       = table.Column<int>(nullable: false),
                StartOffset  = table.Column<int>(nullable: false),
                EndOffset    = table.Column<int>(nullable: false),
                SelectedText = table.Column<string>(maxLength: 1000, nullable: false),
                Status       = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "Open"),
                IsDeleted    = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedBy    = table.Column<string>(nullable: false, defaultValue: ""),
                CreatedAt    = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_InlineAnnotations", x => x.Id);
                t.ForeignKey("FK_InlineAnnotations_Pages_PageId",
                    x => x.PageId, "Pages", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            "IX_InlineAnnotations_PageId", "InlineAnnotations", "PageId");
        migrationBuilder.CreateIndex(
            "IX_InlineAnnotations_PageId_Status",
            "InlineAnnotations", new[] { "PageId", "Status" });

        // ── AnnotationReplies ─────────────────────────────────
        migrationBuilder.CreateTable(
            name: "AnnotationReplies",
            columns: table => new
            {
                Id           = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AnnotationId = table.Column<int>(nullable: false),
                Body         = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AuthorSid    = table.Column<string>(maxLength: 100, nullable: false),
                IsDeleted    = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedAt    = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt    = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_AnnotationReplies", x => x.Id);
                t.ForeignKey("FK_AnnotationReplies_InlineAnnotations_AnnotationId",
                    x => x.AnnotationId, "InlineAnnotations", "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            "IX_AnnotationReplies_AnnotationId",
            "AnnotationReplies", "AnnotationId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AnnotationReplies");
        migrationBuilder.DropTable("InlineAnnotations");
        migrationBuilder.DropTable("PageTemplates");
    }
}
