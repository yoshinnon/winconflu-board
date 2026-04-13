// ============================================================
// WinConflu.NET — ProseMirrorService
// ProseMirror JSON ↔ Markdown / HTML / PlainText 変換
// MarkdownService の役割分担:
//   MarkdownService    → 閲覧用 HTML レンダリング（Markdown入力）
//   ProseMirrorService → WYSIWYG JSON の保存・検索・閲覧変換
// ============================================================

using System.Text;
using System.Text.Json;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace WinConflu.Services;

public interface IProseMirrorService
{
    /// <summary>ProseMirror JSON → HTML（閲覧用）</summary>
    string ToHtml(string json);

    /// <summary>ProseMirror JSON → Markdown（エクスポート用）</summary>
    string ToMarkdown(string json);

    /// <summary>ProseMirror JSON → プレーンテキスト（検索インデックス用）</summary>
    string ToPlainText(string json);

    /// <summary>Markdown → ProseMirror JSON（既存データの初回インポート用）</summary>
    string FromMarkdown(string markdown);

    /// <summary>JSON が有効な ProseMirror doc かチェック</summary>
    bool IsValidDoc(string json);
}

public class ProseMirrorService : IProseMirrorService
{
    private static readonly MarkdownPipeline _mdPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseTaskLists()
        .DisableHtml()
        .Build();

    // ── JSON → HTML ──────────────────────────────────────────

    public string ToHtml(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var sb  = new StringBuilder();
            RenderNodeToHtml(doc, sb);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"<p class='wcn-render-error'>コンテンツの表示に失敗しました: {ex.Message}</p>";
        }
    }

    private static void RenderNodeToHtml(JsonElement node, StringBuilder sb)
    {
        if (!node.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString() ?? string.Empty;

        var hasContent = node.TryGetProperty("content", out var content);
        var attrs      = node.TryGetProperty("attrs", out var a) ? a : default;

        switch (type)
        {
            case "doc":
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                break;

            case "paragraph":
                sb.Append("<p>");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</p>\n");
                break;

            case "heading":
                var level = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("level", out var lvl) ? lvl.GetInt32() : 1;
                sb.Append($"<h{level} id=\"{ExtractIdFromContent(node)}\">");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append($"</h{level}>\n");
                break;

            case "text":
                var text = node.TryGetProperty("text", out var textEl) ? System.Net.WebUtility.HtmlEncode(textEl.GetString() ?? "") : "";
                if (node.TryGetProperty("marks", out var marks))
                    text = ApplyMarks(text, marks);
                sb.Append(text);
                break;

            case "bulletList":
                sb.Append("<ul>\n");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</ul>\n");
                break;

            case "orderedList":
                sb.Append("<ol>\n");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</ol>\n");
                break;

            case "listItem":
                sb.Append("<li>");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</li>\n");
                break;

            case "taskList":
                sb.Append("<ul class=\"task-list\">\n");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</ul>\n");
                break;

            case "taskItem":
                var checked_ = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("checked", out var ck) && ck.GetBoolean();
                sb.Append($"<li class=\"task-item{(checked_ ? " checked" : "")}\"><input type=\"checkbox\" disabled{(checked_ ? " checked" : "")} />");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</li>\n");
                break;

            case "codeBlock":
                var lang = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("language", out var langEl) ? langEl.GetString() ?? "" : "";
                sb.Append($"<pre class=\"wcn-code\" data-lang=\"{lang}\"><code class=\"language-{lang}\">");
                if (hasContent) foreach (var child in content.EnumerateArray())
                {
                    if (child.TryGetProperty("text", out var codeText))
                        sb.Append(System.Net.WebUtility.HtmlEncode(codeText.GetString() ?? ""));
                }
                sb.Append("</code></pre>\n");
                break;

            case "blockquote":
                sb.Append("<blockquote>");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</blockquote>\n");
                break;

            case "horizontalRule":
                sb.Append("<hr />\n");
                break;

            case "hardBreak":
                sb.Append("<br />");
                break;

            case "image":
                var src = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("src", out var srcEl) ? srcEl.GetString() ?? "" : "";
                var alt = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("alt", out var altEl) ? altEl.GetString() ?? "" : "";
                sb.Append($"<img src=\"{System.Net.WebUtility.HtmlAttributeEncode(src)}\" alt=\"{System.Net.WebUtility.HtmlAttributeEncode(alt)}\" class=\"wcn-image\" />\n");
                break;

            case "table":
                sb.Append("<table class=\"wcn-table\">\n");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</table>\n");
                break;

            case "tableRow":
                sb.Append("<tr>");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</tr>\n");
                break;

            case "tableHeader":
                var colspan_h = GetAttrInt(attrs, "colspan", 1);
                var rowspan_h = GetAttrInt(attrs, "rowspan", 1);
                sb.Append($"<th colspan=\"{colspan_h}\" rowspan=\"{rowspan_h}\">");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</th>");
                break;

            case "tableCell":
                var colspan_c = GetAttrInt(attrs, "colspan", 1);
                var rowspan_c = GetAttrInt(attrs, "rowspan", 1);
                sb.Append($"<td colspan=\"{colspan_c}\" rowspan=\"{rowspan_c}\">");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</td>");
                break;

            case "infoBox":
                var variant = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("variant", out var vEl) ? vEl.GetString() ?? "info" : "info";
                var icons   = new Dictionary<string, string> { ["info"] = "ℹ️", ["warning"] = "⚠️", ["tip"] = "💡", ["danger"] = "🚨" };
                sb.Append($"<div class=\"wcn-infobox wcn-infobox--{variant}\">");
                sb.Append($"<div class=\"wcn-infobox__icon\">{icons.GetValueOrDefault(variant, "ℹ️")}</div>");
                sb.Append("<div class=\"wcn-infobox__body\">");
                if (hasContent) foreach (var child in content.EnumerateArray()) RenderNodeToHtml(child, sb);
                sb.Append("</div></div>\n");
                break;

            case "issueEmbed":
                var key      = GetAttrStr(attrs, "issueKey");
                var title    = GetAttrStr(attrs, "title");
                var status   = GetAttrStr(attrs, "status");
                sb.Append($"<span class=\"wcn-issue-embed\" data-type=\"issue-embed\" data-key=\"{key}\">");
                sb.Append($"<span class=\"wcn-issue-embed__key\">{key}</span> ");
                sb.Append($"<span class=\"wcn-issue-embed__title\">{System.Net.WebUtility.HtmlEncode(title)}</span> ");
                sb.Append($"<span class=\"wcn-issue-embed__status\">{status}</span>");
                sb.Append("</span>");
                break;

            case "mention":
                var mentionId = GetAttrStr(attrs, "id");
                var label     = GetAttrStr(attrs, "label");
                sb.Append($"<span class=\"wcn-mention\" data-id=\"{mentionId}\">@{System.Net.WebUtility.HtmlEncode(label)}</span>");
                break;
        }
    }

    // ── JSON → Markdown ──────────────────────────────────────

    public string ToMarkdown(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var sb  = new StringBuilder();
            NodeToMarkdown(doc, sb, 0);
            return sb.ToString().Trim();
        }
        catch { return string.Empty; }
    }

    private static void NodeToMarkdown(JsonElement node, StringBuilder sb, int depth)
    {
        if (!node.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString() ?? string.Empty;
        var hasContent = node.TryGetProperty("content", out var content);
        var attrs = node.TryGetProperty("attrs", out var a) ? a : default;

        switch (type)
        {
            case "doc":
                if (hasContent) foreach (var child in content.EnumerateArray()) NodeToMarkdown(child, sb, depth);
                break;
            case "paragraph":
                if (hasContent) foreach (var child in content.EnumerateArray()) NodeToMarkdown(child, sb, depth);
                sb.Append("\n\n");
                break;
            case "heading":
                var level = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("level", out var lvl) ? lvl.GetInt32() : 1;
                sb.Append(new string('#', level) + " ");
                if (hasContent) foreach (var child in content.EnumerateArray()) NodeToMarkdown(child, sb, depth);
                sb.Append("\n\n");
                break;
            case "text":
                var text = node.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                if (node.TryGetProperty("marks", out var marks))
                    text = ApplyMarksMarkdown(text, marks);
                sb.Append(text);
                break;
            case "bulletList":
                if (hasContent) foreach (var child in content.EnumerateArray()) { sb.Append("- "); NodeToMarkdown(child, sb, depth + 1); }
                sb.Append("\n");
                break;
            case "orderedList":
                int i = 1;
                if (hasContent) foreach (var child in content.EnumerateArray()) { sb.Append($"{i++}. "); NodeToMarkdown(child, sb, depth + 1); }
                sb.Append("\n");
                break;
            case "listItem":
                if (hasContent) foreach (var child in content.EnumerateArray()) NodeToMarkdown(child, sb, depth);
                break;
            case "taskItem":
                var chk = attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty("checked", out var ck) && ck.GetBoolean();
                sb.Append(chk ? "- [x] " : "- [ ] ");
                if (hasContent) foreach (var child in content.EnumerateArray()) NodeToMarkdown(child, sb, depth);
                break;
            case "codeBlock":
                var lang = GetAttrStr(attrs, "language");
                sb.Append($"```{lang}\n");
                if (hasContent) foreach (var child in content.EnumerateArray()) { if (child.TryGetProperty("text", out var ct)) sb.Append(ct.GetString()); }
                sb.Append("\n```\n\n");
                break;
            case "blockquote":
                var inner = new StringBuilder();
                if (hasContent) foreach (var child in content.EnumerateArray()) NodeToMarkdown(child, inner, depth);
                foreach (var line in inner.ToString().Split('\n'))
                    sb.Append($"> {line}\n");
                sb.Append("\n");
                break;
            case "horizontalRule":
                sb.Append("---\n\n");
                break;
            case "hardBreak":
                sb.Append("  \n");
                break;
            case "image":
                sb.Append($"![{GetAttrStr(attrs, "alt")}]({GetAttrStr(attrs, "src")})\n\n");
                break;
            case "infoBox":
                var variant = GetAttrStr(attrs, "variant", "info");
                sb.Append($":::{ variant}\n");
                if (hasContent) foreach (var child in content.EnumerateArray()) NodeToMarkdown(child, sb, depth);
                sb.Append(":::\n\n");
                break;
            case "issueEmbed":
                sb.Append($"[[{GetAttrStr(attrs, "issueKey")}]]");
                break;
            case "mention":
                sb.Append($"@{GetAttrStr(attrs, "label")}");
                break;
        }
    }

    // ── JSON → プレーンテキスト（FTS 用） ────────────────────

    public string ToPlainText(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var sb  = new StringBuilder();
            ExtractText(doc, sb);
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    private static void ExtractText(JsonElement node, StringBuilder sb)
    {
        if (node.TryGetProperty("text", out var textEl))
            sb.Append(textEl.GetString()).Append(' ');
        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var child in content.EnumerateArray()) ExtractText(child, sb);
    }

    // ── Markdown → ProseMirror JSON ──────────────────────────

    public string FromMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "{\"type\":\"doc\",\"content\":[]}";
        // Markdown を HTML に変換し、Tiptap がパースできる形式で返す
        // 実際の変換は JS 側の setMarkdown で処理されるため、
        // ここでは HTML 形式の文字列を返す
        var html = Markdown.ToHtml(markdown, _mdPipeline);
        // HTML をエスケープして JS 側に渡すための JSON ラッパー
        return JsonSerializer.Serialize(new { type = "html", content = html });
    }

    public bool IsValidDoc(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            return doc.TryGetProperty("type", out var t) && t.GetString() == "doc";
        }
        catch { return false; }
    }

    // ── マーク適用ヘルパー ────────────────────────────────────

    private static string ApplyMarks(string text, JsonElement marks)
    {
        foreach (var mark in marks.EnumerateArray())
        {
            if (!mark.TryGetProperty("type", out var t)) continue;
            text = t.GetString() switch
            {
                "bold"       => $"<strong>{text}</strong>",
                "italic"     => $"<em>{text}</em>",
                "underline"  => $"<u>{text}</u>",
                "strike"     => $"<s>{text}</s>",
                "code"       => $"<code>{text}</code>",
                "highlight"  => $"<mark>{text}</mark>",
                "link"       => WrapLink(text, mark),
                _            => text
            };
        }
        return text;
    }

    private static string WrapLink(string text, JsonElement mark)
    {
        var href = mark.TryGetProperty("attrs", out var a) && a.TryGetProperty("href", out var h)
            ? h.GetString() ?? "#" : "#";
        return $"<a href=\"{System.Net.WebUtility.HtmlAttributeEncode(href)}\" target=\"_blank\" rel=\"noopener\">{text}</a>";
    }

    private static string ApplyMarksMarkdown(string text, JsonElement marks)
    {
        foreach (var mark in marks.EnumerateArray())
        {
            if (!mark.TryGetProperty("type", out var t)) continue;
            text = t.GetString() switch
            {
                "bold"      => $"**{text}**",
                "italic"    => $"*{text}*",
                "strike"    => $"~~{text}~~",
                "code"      => $"`{text}`",
                "underline" => $"<u>{text}</u>",
                _           => text
            };
        }
        return text;
    }

    private static string ExtractIdFromContent(JsonElement node)
    {
        var sb = new StringBuilder();
        ExtractText(node, sb);
        return sb.ToString().Trim().ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("　", "-");
    }

    private static int GetAttrInt(JsonElement attrs, string key, int def)
        => attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
           ? v.GetInt32() : def;

    private static string GetAttrStr(JsonElement attrs, string key, string def = "")
        => attrs.ValueKind != JsonValueKind.Undefined && attrs.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
           ? v.GetString() ?? def : def;
}
