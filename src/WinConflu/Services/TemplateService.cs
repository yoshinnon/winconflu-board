// ============================================================
// WinConflu.NET — TemplateService
// テンプレート管理 + 組み込みテンプレートのシード
// ============================================================

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WinConflu.Data;
using WinConflu.Models;

namespace WinConflu.Services;

public interface ITemplateService
{
    Task<List<PageTemplate>>      GetAllAsync(string? category = null, CancellationToken ct = default);
    Task<PageTemplate?>           GetAsync(int id, CancellationToken ct = default);
    Task<PageTemplate>            CreateAsync(CreateTemplateRequest req, string authorSid, CancellationToken ct = default);
    Task<PageTemplate>            UpdateAsync(int id, UpdateTemplateRequest req, CancellationToken ct = default);
    Task                          DeleteAsync(int id, CancellationToken ct = default);
    Task<string>                  RenderAsync(int id, Dictionary<string, string> variables, CancellationToken ct = default);
    Task                          EnsureBuiltInSeedAsync(CancellationToken ct = default);
    Task<List<string>>            GetCategoriesAsync(CancellationToken ct = default);
}

public record CreateTemplateRequest(
    string Name, string Description, string Category,
    string IconName, string Content, string VariablesJson);

public record UpdateTemplateRequest(
    string Name, string Description, string Category,
    string IconName, string Content, string VariablesJson);

public class TemplateService(AppDbContext db) : ITemplateService
{
    public async Task<List<PageTemplate>> GetAllAsync(
        string? category = null, CancellationToken ct = default)
    {
        var q = db.PageTemplates.Where(t => !t.IsDeleted);
        if (!string.IsNullOrEmpty(category))
            q = q.Where(t => t.Category == category);
        return await q.OrderByDescending(t => t.IsBuiltIn)
                      .ThenByDescending(t => t.UsageCount)
                      .ThenBy(t => t.Name)
                      .ToListAsync(ct);
    }

    public async Task<PageTemplate?> GetAsync(int id, CancellationToken ct = default)
        => await db.PageTemplates.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);

    public async Task<PageTemplate> CreateAsync(
        CreateTemplateRequest req, string authorSid, CancellationToken ct = default)
    {
        var tmpl = new PageTemplate
        {
            Name          = req.Name,
            Description   = req.Description,
            Category      = req.Category,
            IconName      = req.IconName,
            Content       = req.Content,
            VariablesJson = req.VariablesJson,
            CreatedBy     = authorSid
        };
        db.PageTemplates.Add(tmpl);
        await db.SaveChangesAsync(ct);
        return tmpl;
    }

    public async Task<PageTemplate> UpdateAsync(
        int id, UpdateTemplateRequest req, CancellationToken ct = default)
    {
        var tmpl = await db.PageTemplates.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"テンプレート {id} が見つかりません。");
        if (tmpl.IsBuiltIn)
            throw new InvalidOperationException("組み込みテンプレートは編集できません。");

        tmpl.Name          = req.Name;
        tmpl.Description   = req.Description;
        tmpl.Category      = req.Category;
        tmpl.IconName      = req.IconName;
        tmpl.Content       = req.Content;
        tmpl.VariablesJson = req.VariablesJson;
        tmpl.UpdatedAt     = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return tmpl;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var tmpl = await db.PageTemplates.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"テンプレート {id} が見つかりません。");
        if (tmpl.IsBuiltIn)
            throw new InvalidOperationException("組み込みテンプレートは削除できません。");
        tmpl.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    // ── テンプレート変数の展開 ────────────────────────────
    public async Task<string> RenderAsync(
        int id, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        var tmpl = await GetAsync(id, ct)
            ?? throw new InvalidOperationException($"テンプレート {id} が見つかりません。");

        // 使用回数をインクリメント
        tmpl.UsageCount++;
        await db.SaveChangesAsync(ct);

        var content = tmpl.Content;

        // 組み込み変数
        content = content
            .Replace("{{TODAY}}",     DateTimeOffset.Now.ToString("yyyy年MM月dd日"))
            .Replace("{{DATETIME}}", DateTimeOffset.Now.ToString("yyyy年MM月dd日 HH:mm"))
            .Replace("{{YEAR}}",     DateTimeOffset.Now.Year.ToString())
            .Replace("{{MONTH}}",    DateTimeOffset.Now.Month.ToString("D2"))
            .Replace("{{DAY}}",      DateTimeOffset.Now.Day.ToString("D2"));

        // ユーザー指定変数
        foreach (var (key, value) in variables)
            content = content.Replace($"{{{{{key}}}}}", value);

        return content;
    }

    public async Task<List<string>> GetCategoriesAsync(CancellationToken ct = default)
        => await db.PageTemplates
            .Where(t => !t.IsDeleted)
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

    // ── 組み込みテンプレートのシード ─────────────────────
    public async Task EnsureBuiltInSeedAsync(CancellationToken ct = default)
    {
        if (await db.PageTemplates.AnyAsync(t => t.IsBuiltIn, ct)) return;

        var builtIns = BuiltInTemplates();
        db.PageTemplates.AddRange(builtIns);
        await db.SaveChangesAsync(ct);
    }

    private static List<PageTemplate> BuiltInTemplates() =>
    [
        new PageTemplate
        {
            Name        = "議事録",
            Description = "会議・MTGの記録に使用するテンプレート",
            Category    = "会議",
            IconName    = "EventNote",
            IsBuiltIn   = true,
            VariablesJson = JsonSerializer.Serialize(new[]
            {
                new { Key = "MEETING_TITLE", Label = "会議名", DefaultValue = "", InputType = "text" },
                new { Key = "ATTENDEES",     Label = "参加者",  DefaultValue = "", InputType = "text" },
                new { Key = "LOCATION",      Label = "場所",    DefaultValue = "Teams / オンライン", InputType = "text" }
            }),
            Content = """
                # {{MEETING_TITLE}}

                **日時:** {{TODAY}}
                **場所:** {{LOCATION}}
                **参加者:** {{ATTENDEES}}

                ---

                ## アジェンダ

                1.
                2.
                3.

                ---

                ## 議事内容

                ### 1. 

                ### 2. 

                ### 3. 

                ---

                ## 決定事項

                | # | 内容 | 担当 | 期限 |
                |---|------|------|------|
                | 1 |      |      |      |

                ---

                ## 次回アクション

                - [ ] 
                - [ ] 

                ---

                ## 次回開催

                **日時:** 未定
                **場所:** 
                """,
            CreatedBy = "system"
        },

        new PageTemplate
        {
            Name        = "仕様書",
            Description = "機能・システムの仕様を記述するテンプレート",
            Category    = "開発",
            IconName    = "Article",
            IsBuiltIn   = true,
            VariablesJson = JsonSerializer.Serialize(new[]
            {
                new { Key = "FEATURE_NAME", Label = "機能名",   DefaultValue = "", InputType = "text" },
                new { Key = "AUTHOR",       Label = "作成者",   DefaultValue = "", InputType = "text" },
                new { Key = "VERSION",      Label = "バージョン", DefaultValue = "1.0", InputType = "text" }
            }),
            Content = """
                # {{FEATURE_NAME}} 仕様書

                | 項目 | 内容 |
                |------|------|
                | 作成者 | {{AUTHOR}} |
                | 作成日 | {{TODAY}} |
                | バージョン | {{VERSION}} |
                | ステータス | 草稿 |

                ---

                ## 1. 概要

                > この機能が解決する問題や目的を1〜3文で記述してください。

                ---

                ## 2. 背景・目的

                ### 2.1 現状の課題

                ### 2.2 実現したいこと

                ---

                ## 3. 機能要件

                ### 3.1 ユーザーストーリー

                - ユーザーとして、〜できる
                - 管理者として、〜できる

                ### 3.2 機能一覧

                | # | 機能 | 優先度 | 備考 |
                |---|------|--------|------|
                | 1 |      | Must   |      |
                | 2 |      | Should |      |

                ---

                ## 4. 非機能要件

                - **パフォーマンス:** レスポンス 200ms 以内
                - **セキュリティ:** 
                - **可用性:** 

                ---

                ## 5. 画面・UI 設計

                > スクリーンショットやワイヤーフレームを添付してください。

                ---

                ## 6. API 設計

                ### エンドポイント

                ```
                GET /api/v1/
                POST /api/v1/
                ```

                ---

                ## 7. データモデル

                ---

                ## 8. 外部依存・連携

                ---

                ## 9. 未決事項 / TODO

                - [ ] 
                - [ ] 

                ---

                ## 10. 変更履歴

                | バージョン | 日付 | 変更者 | 内容 |
                |-----------|------|--------|------|
                | 1.0 | {{TODAY}} | {{AUTHOR}} | 初版作成 |
                """,
            CreatedBy = "system"
        },

        new PageTemplate
        {
            Name        = "日報",
            Description = "日次作業報告のテンプレート",
            Category    = "管理",
            IconName    = "Today",
            IsBuiltIn   = true,
            VariablesJson = JsonSerializer.Serialize(new[]
            {
                new { Key = "AUTHOR", Label = "報告者", DefaultValue = "", InputType = "text" }
            }),
            Content = """
                # 日報 {{TODAY}}

                **報告者:** {{AUTHOR}}

                ---

                ## 本日の作業

                | # | 作業内容 | 状況 | 所要時間 |
                |---|----------|------|----------|
                | 1 |          | 完了 |          |
                | 2 |          | 対応中 |        |

                ---

                ## 完了したタスク

                - [ ] 
                - [ ] 

                ---

                ## 明日の予定

                - [ ] 
                - [ ] 

                ---

                ## 課題・ブロッカー

                > 進捗を妨げている問題があれば記載してください。

                ---

                ## 連絡事項

                """,
            CreatedBy = "system"
        },

        new PageTemplate
        {
            Name        = "障害報告書",
            Description = "システム障害・インシデントの記録と分析",
            Category    = "運用",
            IconName    = "BugReport",
            IsBuiltIn   = true,
            VariablesJson = JsonSerializer.Serialize(new[]
            {
                new { Key = "INCIDENT_TITLE", Label = "インシデント名", DefaultValue = "", InputType = "text" },
                new { Key = "SEVERITY",       Label = "重大度",  DefaultValue = "P2 - 高", InputType = "text" }
            }),
            Content = """
                # 障害報告書: {{INCIDENT_TITLE}}

                | 項目 | 内容 |
                |------|------|
                | 発生日時 | {{DATETIME}} |
                | 重大度 | {{SEVERITY}} |
                | ステータス | 対応中 |

                ---

                ## 1. 障害概要

                ### 発生事象

                ### 影響範囲

                - 影響ユーザー数:
                - 影響システム:
                - ダウンタイム:

                ---

                ## 2. タイムライン

                | 日時 | 事象 | 対応者 |
                |------|------|--------|
                | {{DATETIME}} | 障害検知 | |
                |  | 一次対応開始 | |
                |  | 復旧完了 | |

                ---

                ## 3. 根本原因 (RCA)

                ### 直接原因

                ### 根本原因

                ---

                ## 4. 対応内容

                ### 緊急対応（暫定処置）

                ### 恒久対応

                ---

                ## 5. 再発防止策

                | # | 対策 | 担当 | 期限 |
                |---|------|------|------|
                | 1 |      |      |      |

                ---

                ## 6. 教訓・改善点

                """,
            CreatedBy = "system"
        },

        new PageTemplate
        {
            Name        = "週次レポート",
            Description = "プロジェクトの週次進捗レポート",
            Category    = "管理",
            IconName    = "Assessment",
            IsBuiltIn   = true,
            VariablesJson = JsonSerializer.Serialize(new[]
            {
                new { Key = "PROJECT_NAME", Label = "プロジェクト名", DefaultValue = "", InputType = "text" },
                new { Key = "PERIOD",       Label = "対象期間",       DefaultValue = "", InputType = "text" }
            }),
            Content = """
                # 週次レポート: {{PROJECT_NAME}}

                **対象期間:** {{PERIOD}}
                **作成日:** {{TODAY}}

                ---

                ## サマリー

                > 今週の最重要トピックを2〜3文で記述してください。

                ---

                ## 今週の進捗

                | タスク | 状態 | 完了率 | 備考 |
                |--------|------|--------|------|
                |        | 完了 | 100%   |      |
                |        | 対応中 | 60%  |      |

                ---

                ## KPI / 指標

                | 指標 | 目標 | 実績 | 達成率 |
                |------|------|------|--------|
                |      |      |      |        |

                ---

                ## リスク・課題

                | # | 内容 | 影響度 | 対応策 |
                |---|------|--------|--------|
                | 1 |      | 高     |        |

                ---

                ## 来週の予定

                - 
                - 

                ---

                ## 連絡事項・依頼

                """,
            CreatedBy = "system"
        }
    ];
}
