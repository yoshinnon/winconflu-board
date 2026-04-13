// ============================================================
// WinConflu.NET — InlineAnnotationService
// テキスト選択注釈の作成・スレッド管理・アンカーずれ検出
// ============================================================

using Microsoft.EntityFrameworkCore;
using WinConflu.Data;
using WinConflu.Models;

namespace WinConflu.Services;

public interface IInlineAnnotationService
{
    Task<List<InlineAnnotation>> GetByPageAsync(int pageId, bool includeResolved = false, CancellationToken ct = default);
    Task<InlineAnnotation>       CreateAsync(CreateAnnotationRequest req, string authorSid, CancellationToken ct = default);
    Task<AnnotationReply>        AddReplyAsync(int annotationId, string body, string authorSid, CancellationToken ct = default);
    Task                         ResolveAsync(int annotationId, string authorSid, CancellationToken ct = default);
    Task                         DeleteAsync(int annotationId, string authorSid, CancellationToken ct = default);
    Task                         DeleteReplyAsync(int replyId, string authorSid, CancellationToken ct = default);

    /// <summary>
    /// ページ本文が更新された際に、アンカーのずれを検出して
    /// AnnotationStatus.Outdated に変更する。
    /// </summary>
    Task RecalculateAnchorsAsync(int pageId, string newContent, CancellationToken ct = default);
}

public record CreateAnnotationRequest(
    int    PageId,
    int    StartOffset,
    int    EndOffset,
    string SelectedText,
    string FirstReplyBody   // 注釈作成と同時に最初のコメントを投稿
);

public class InlineAnnotationService(
    AppDbContext db,
    IAuditService audit) : IInlineAnnotationService
{
    // ── 取得 ─────────────────────────────────────────────────

    public async Task<List<InlineAnnotation>> GetByPageAsync(
        int pageId, bool includeResolved = false, CancellationToken ct = default)
    {
        var q = db.InlineAnnotations
            .Include(a => a.Replies.Where(r => !r.IsDeleted))
            .Where(a => a.PageId == pageId && !a.IsDeleted);

        if (!includeResolved)
            q = q.Where(a => a.Status == AnnotationStatus.Open);

        return await q.OrderBy(a => a.StartOffset).ToListAsync(ct);
    }

    // ── 注釈作成（最初の返信もセットで投稿） ────────────────

    public async Task<InlineAnnotation> CreateAsync(
        CreateAnnotationRequest req, string authorSid, CancellationToken ct = default)
    {
        // 選択テキストが実際のページ本文と一致するか検証
        var page = await db.Pages.FindAsync([req.PageId], ct)
            ?? throw new InvalidOperationException($"ページ {req.PageId} が見つかりません。");

        ValidateAnchor(page.Content, req.StartOffset, req.EndOffset, req.SelectedText);

        var annotation = new InlineAnnotation
        {
            PageId       = req.PageId,
            StartOffset  = req.StartOffset,
            EndOffset    = req.EndOffset,
            SelectedText = req.SelectedText,
            Status       = AnnotationStatus.Open,
            CreatedBy    = authorSid
        };

        // 最初のコメントを同時に追加
        annotation.Replies.Add(new AnnotationReply
        {
            Body      = req.FirstReplyBody,
            AuthorSid = authorSid
        });

        db.InlineAnnotations.Add(annotation);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Create", "InlineAnnotation", annotation.Id, authorSid, null, req.SelectedText);
        return annotation;
    }

    // ── 返信追加 ─────────────────────────────────────────────

    public async Task<AnnotationReply> AddReplyAsync(
        int annotationId, string body, string authorSid, CancellationToken ct = default)
    {
        var annotation = await db.InlineAnnotations
            .Include(a => a.Replies)
            .FirstOrDefaultAsync(a => a.Id == annotationId && !a.IsDeleted, ct)
            ?? throw new InvalidOperationException($"注釈 {annotationId} が見つかりません。");

        if (annotation.Status == AnnotationStatus.Resolved)
            throw new InvalidOperationException("解決済みの注釈にはコメントを追加できません。解決を取り消してください。");

        var reply = new AnnotationReply
        {
            AnnotationId = annotationId,
            Body         = body,
            AuthorSid    = authorSid
        };

        db.AnnotationReplies.Add(reply);
        await db.SaveChangesAsync(ct);
        return reply;
    }

    // ── 解決 ─────────────────────────────────────────────────

    public async Task ResolveAsync(
        int annotationId, string authorSid, CancellationToken ct = default)
    {
        var annotation = await db.InlineAnnotations.FindAsync([annotationId], ct)
            ?? throw new InvalidOperationException($"注釈 {annotationId} が見つかりません。");

        // 解決済みの場合は Open に戻す（トグル）
        annotation.Status = annotation.Status == AnnotationStatus.Open
            ? AnnotationStatus.Resolved
            : AnnotationStatus.Open;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Resolve", "InlineAnnotation", annotationId, authorSid,
            null, annotation.Status.ToString());
    }

    // ── 削除 ─────────────────────────────────────────────────

    public async Task DeleteAsync(
        int annotationId, string authorSid, CancellationToken ct = default)
    {
        var annotation = await db.InlineAnnotations.FindAsync([annotationId], ct)
            ?? throw new InvalidOperationException($"注釈 {annotationId} が見つかりません。");

        annotation.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Delete", "InlineAnnotation", annotationId, authorSid, annotation.SelectedText, null);
    }

    public async Task DeleteReplyAsync(
        int replyId, string authorSid, CancellationToken ct = default)
    {
        var reply = await db.AnnotationReplies.FindAsync([replyId], ct)
            ?? throw new InvalidOperationException($"返信 {replyId} が見つかりません。");

        reply.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    // ── ページ更新後のアンカー再計算 ─────────────────────────

    public async Task RecalculateAnchorsAsync(
        int pageId, string newContent, CancellationToken ct = default)
    {
        var annotations = await db.InlineAnnotations
            .Where(a => a.PageId == pageId
                     && !a.IsDeleted
                     && a.Status == AnnotationStatus.Open)
            .ToListAsync(ct);

        if (annotations.Count == 0) return;

        bool changed = false;
        foreach (var ann in annotations)
        {
            // 新本文の同じオフセット範囲のテキストを取り出して比較
            if (!IsAnchorValid(newContent, ann.StartOffset, ann.EndOffset, ann.SelectedText))
            {
                ann.Status = AnnotationStatus.Outdated;
                changed    = true;
            }
        }

        if (changed) await db.SaveChangesAsync(ct);
    }

    // ── プライベートヘルパー ──────────────────────────────────

    private static void ValidateAnchor(
        string content, int start, int end, string selectedText)
    {
        if (start < 0 || end > content.Length || start >= end)
            throw new ArgumentException("オフセット範囲が不正です。");

        var actual = content[start..end];
        if (actual != selectedText)
            throw new ArgumentException(
                $"選択テキストが本文と一致しません。期待: '{selectedText}', 実際: '{actual}'");
    }

    private static bool IsAnchorValid(
        string content, int start, int end, string selectedText)
    {
        if (start < 0 || end > content.Length || start >= end) return false;
        return content[start..end] == selectedText;
    }
}
