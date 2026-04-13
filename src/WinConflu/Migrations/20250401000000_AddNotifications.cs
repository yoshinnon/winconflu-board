// ============================================================
// WinConflu.NET — マイグレーション: 通知テーブル
// Hangfire のスキーマは UseHangfireSqlServerStorage が自動作成するため
// ここでは AppNotifications のみ管理する
// ============================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinConflu.Migrations;

public partial class AddNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppNotifications",
            columns: table => new
            {
                Id           = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RecipientSid = table.Column<string>(maxLength: 100, nullable: false),
                Title        = table.Column<string>(maxLength: 200, nullable: false),
                Body         = table.Column<string>(maxLength: 1000, nullable: false, defaultValue: ""),
                Kind         = table.Column<string>(maxLength: 50, nullable: false),
                LinkUrl      = table.Column<string>(maxLength: 500, nullable: true),
                IsRead       = table.Column<bool>(nullable: false, defaultValue: false),
                CreatedAt    = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_AppNotifications", x => x.Id));

        migrationBuilder.CreateIndex(
            "IX_AppNotifications_RecipientSid_IsRead",
            "AppNotifications",
            new[] { "RecipientSid", "IsRead" });

        migrationBuilder.CreateIndex(
            "IX_AppNotifications_CreatedAt",
            "AppNotifications",
            "CreatedAt");

        // 古い通知を自動削除するジョブは Hangfire 側で管理
        // 90日を超えた通知は定期削除: SELECT で実装
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("AppNotifications");
}
