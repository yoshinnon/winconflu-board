// ============================================================
// WinConflu.NET — MarkdownService
// Markdig によるレンダリング / 目次生成 / アノテーションハイライト
// ============================================================

using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;
using System.Text.RegularExpressions;
using WinConflu.Models;

namespace WinConflu.Services;

public interface IMarkdownService
{
    /// <summary>Markdown → HTML（サニタイズ済み）</summary>
    string Render(string markdown);

    /// <summary>Markdown → HTML + アノテーションハイライト注入</summary>
    string RenderWithAnnotations(string markdown, IEnumerable<InlineAnnotation> annotations);

    /// <summary>見出しを自動抽出して目次 HTML を生成</summary>
    string GenerateToc(string markdown, int maxDepth = 3);

    /// <summary>プレーンテキスト抽出（検索スニペット生成用）</summary>
    string ToPlainText(string markdown, int maxLength = 200);
}

public class MarkdownService : IMarkdownService
{
    // Markdig パイプライン：表・コードブロック・AutoId など一括有効
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseTaskLists()
        .UseEmojiAndSmiley()
        .UseYamlFrontMatter()
        .DisableHtml()          // 生 HTML インジェクション防止
        .Build();

    // ── 基本レンダリング ──────────────────────────────────────

    public string Render(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;
        var html = Markdown.ToHtml(markdown, _pipeline);
        return WrapCodeBlocks(html);
    }

    // ── アノテーションハイライト注入 ────────────────────────
    // 本文の文字オフセットに対応する位置に <mark> タグを差し込む

    public string RenderWithAnnotations(
        string markdown, IEnumerable<InlineAnnotation> annotations)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;

        var annList = annotations
            .Where(a => !a.IsDeleted && a.Status != AnnotationStatus.Outdated)
            .OrderBy(a => a.StartOffset)
            .ToList();

        if (annList.Count == 0) return Render(markdown);

        // オフセットはMarkdown本文基準なので、先にプレーンテキスト上でタグを注入してからレンダリング
        var annotated = InjectAnnotationMarkers(markdown, annList);
        var html      = Markdown.ToHtml(annotated, _pipeline);
        return WrapCodeBlocks(html);
    }

    private static string InjectAnnotationMarkers(
        string text, List<InlineAnnotation> annotations)
    {
        // 末尾から挿入することでオフセットのずれを防ぐ
        var sb      = new StringBuilder(text);
        var sorted  = annotations.OrderByDescending(a => a.StartOffset).ToList();

        foreach (var ann in sorted)
        {
            if (ann.EndOffset > sb.Length) continue;

            var cssClass = ann.Status == AnnotationStatus.Resolved
                ? "wcn-annotation resolved"
                : "wcn-annotation open";

            sb.Insert(ann.EndOffset,   $"</mark>");
            sb.Insert(ann.StartOffset, $"<mark class=\"{cssClass}\" data-ann-id=\"{ann.Id}\">");
        }

        return sb.ToString();
    }

    // ── 目次（ToC）生成 ──────────────────────────────────────

    public string GenerateToc(string markdown, int maxDepth = 3)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;

        var doc      = Markdown.Parse(markdown, _pipeline);
        var headings = doc.Descendants<HeadingBlock>()
            .Where(h => h.Level <= maxDepth)
            .ToList();

        if (headings.Count < 2) return string.Empty; // 見出し1つは ToC 不要

        var sb = new StringBuilder();
        sb.AppendLine("<nav class=\"wcn-toc\" aria-label=\"目次\">");
        sb.AppendLine("<p class=\"wcn-toc-title\">目次</p>");
        sb.AppendLine("<ol class=\"wcn-toc-list\">");

        int prevLevel = 1;
        foreach (var h in headings)
        {
            var text = ExtractHeadingText(h);
            var slug = GenerateSlug(text);

            while (h.Level > prevLevel) { sb.AppendLine("<ol>"); prevLevel++; }
            while (h.Level < prevLevel) { sb.AppendLine("</ol>"); prevLevel--; }

            sb.AppendLine($"<li><a href=\"#{slug}\">{System.Net.WebUtility.HtmlEncode(text)}</a></li>");
        }

        while (prevLevel > 1) { sb.AppendLine("</ol>"); prevLevel--; }
        sb.AppendLine("</ol></nav>");
        return sb.ToString();
    }

    // ── プレーンテキスト抽出 ─────────────────────────────────

    public string ToPlainText(string markdown, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;
        var html  = Markdown.ToHtml(markdown, _pipeline);
        var plain = Regex.Replace(html, "<[^>]+>", " ");
        plain     = Regex.Replace(plain, @"\s+", " ").Trim();
        return plain.Length <= maxLength ? plain : plain[..maxLength] + "…";
    }

    // ── プライベートヘルパー ──────────────────────────────────

    private static string WrapCodeBlocks(string html)
    {
        // コードブロックにコピーボタン用ラッパーを追加
        return Regex.Replace(html,
            @"<pre><code class=""language-([^""]+)""",
            @"<pre class=""wcn-code"" data-lang=""$1""><code class=""language-$1""");
    }

    private static string ExtractHeadingText(HeadingBlock h)
    {
        var sb = new StringBuilder();
        foreach (var inline in h.Inline?.Descendants<LiteralInline>() ?? [])
            sb.Append(inline.Content.ToString());
        return sb.ToString();
    }

    private static string GenerateSlug(string text)
        => Regex.Replace(text.ToLowerInvariant(), @"[^\w\u3040-\u9FFF\-]", "-")
                .Trim('-');
}
