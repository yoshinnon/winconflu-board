// ============================================================
// WinConflu.NET — マイグレーション: Tiptap WYSIWYG 対応
// Page テーブルに ContentFormat / ContentText 列を追加
// ============================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinConflu.Migrations;

public partial class AddPageContentFormat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // コンテンツ形式フラグ（"markdown" = 既存データ / "json" = Tiptap JSON）
        migrationBuilder.AddColumn<string>(
            name:         "ContentFormat",
            table:        "Pages",
            maxLength:    20,
            nullable:     false,
            defaultValue: "markdown");   // 既存行はすべて Markdown 扱い

        // FTS 用プレーンテキストキャッシュ
        migrationBuilder.AddColumn<string>(
            name:         "ContentText",
            table:        "Pages",
            type:         "nvarchar(max)",
            nullable:     false,
            defaultValue: "");

        // フルテキスト検索インデックスを ContentText にも追加
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.fulltext_indexes fi
                JOIN sys.tables t ON fi.object_id = t.object_id
                WHERE t.name = 'Pages'
            )
            BEGIN
                ALTER FULLTEXT INDEX ON Pages ADD (ContentText);
            END
        ");

        // Revisions テーブルにも同様の列を追加
        migrationBuilder.AddColumn<string>(
            name:         "ContentFormat",
            table:        "Revisions",
            maxLength:    20,
            nullable:     false,
            defaultValue: "markdown");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ContentFormat", "Pages");
        migrationBuilder.DropColumn("ContentText",   "Pages");
        migrationBuilder.DropColumn("ContentFormat", "Revisions");
    }
}
