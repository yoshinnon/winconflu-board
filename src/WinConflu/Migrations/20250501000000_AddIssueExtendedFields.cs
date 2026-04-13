// ============================================================
// WinConflu.NET — マイグレーション: Issue 拡張フィールド追加
// StartDate / StoryPoints / Progress（ロードマップ / スクラム対応）
// ============================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinConflu.Migrations;

public partial class AddIssueExtendedFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ロードマップ用の開始日
        migrationBuilder.AddColumn<DateTimeOffset>(
            name:     "StartDate",
            table:    "Issues",
            nullable: true);

        // スクラム見積もり用ストーリーポイント
        migrationBuilder.AddColumn<int>(
            name:        "StoryPoints",
            table:       "Issues",
            nullable:    true);

        // 完了率 0〜100（サブタスク集計 or 手動入力）
        migrationBuilder.AddColumn<int>(
            name:         "Progress",
            table:        "Issues",
            nullable:     false,
            defaultValue: 0);

        // ロードマップ用インデックス
        migrationBuilder.CreateIndex(
            "IX_Issues_StartDate", "Issues", "StartDate");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Issues_StartDate", "Issues");
        migrationBuilder.DropColumn("StartDate",    "Issues");
        migrationBuilder.DropColumn("StoryPoints",  "Issues");
        migrationBuilder.DropColumn("Progress",     "Issues");
    }
}
