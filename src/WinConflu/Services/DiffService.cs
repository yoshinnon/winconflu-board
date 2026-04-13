// ============================================================
// WinConflu.NET — DiffService
// DiffPlex によるバージョン差分表示（インライン / サイドバイサイド）
// ============================================================

using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace WinConflu.Services;

public interface IDiffService
{
    /// <summary>インライン差分 HTML（旧→新の変更箇所をハイライト）</summary>
    DiffResult BuildInlineDiff(string oldText, string newText);

    /// <summary>サイドバイサイド差分モデル</summary>
    SideBySideDiffModel BuildSideBySide(string oldText, string newText);
}

public record DiffResult(
    List<DiffLine> Lines,
    int AddedCount,
    int DeletedCount,
    int UnchangedCount);

public record DiffLine(
    DiffLineType Type,   // Added / Deleted / Unchanged / Imaginary
    int?   OldLineNo,
    int?   NewLineNo,
    string Text,
    List<DiffPiece>? SubPieces = null);

public enum DiffLineType { Unchanged, Added, Deleted, Modified, Imaginary }

public class DiffService : IDiffService
{
    private static readonly InlineDiffBuilder  _inline    = new(new Differ());
    private static readonly SideBySideDiffBuilder _sideBySide = new(new Differ());

    public DiffResult BuildInlineDiff(string oldText, string newText)
    {
        var model   = _inline.BuildDiffModel(oldText, newText, ignoreWhitespace: false);
        var lines   = new List<DiffLine>();
        int oldLine = 1, newLine = 1;
        int added = 0, deleted = 0, unchanged = 0;

        foreach (var piece in model.Lines)
        {
            var type = piece.Type switch
            {
                ChangeType.Inserted  => DiffLineType.Added,
                ChangeType.Deleted   => DiffLineType.Deleted,
                ChangeType.Modified  => DiffLineType.Modified,
                ChangeType.Imaginary => DiffLineType.Imaginary,
                _                    => DiffLineType.Unchanged
            };

            lines.Add(new DiffLine(
                type,
                type == DiffLineType.Added ? null : oldLine,
                type == DiffLineType.Deleted ? null : newLine,
                piece.Text ?? string.Empty,
                piece.SubPieces?.ToList()));

            switch (type)
            {
                case DiffLineType.Added:     added++;     newLine++; break;
                case DiffLineType.Deleted:   deleted++;   oldLine++; break;
                case DiffLineType.Modified:  added++; deleted++; oldLine++; newLine++; break;
                case DiffLineType.Unchanged: unchanged++; oldLine++; newLine++; break;
            }
        }

        return new DiffResult(lines, added, deleted, unchanged);
    }

    public SideBySideDiffModel BuildSideBySide(string oldText, string newText)
        => _sideBySide.BuildDiffModel(oldText, newText, ignoreWhitespace: false);
}
