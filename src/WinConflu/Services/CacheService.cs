// ============================================================
// WinConflu.NET — CacheService (Phase 5: パフォーマンス最適化)
// 多段キャッシュ戦略 + キャッシュ無効化ヘルパー
// ============================================================

using Microsoft.Extensions.Caching.Memory;

namespace WinConflu.Services;

/// <summary>
/// WinConflu 全体で使うキャッシュの TTL 定数と
/// 一元的な無効化メソッドを提供する。
/// </summary>
public interface ICacheService
{
    T?   Get<T>(string key) where T : class;
    void Set<T>(string key, T value, CacheTier tier) where T : class;
    void Remove(string key);
    void RemoveByPrefix(string prefix);

    // ドメイン固有の一括無効化
    void InvalidateWikiTree(int? parentId = null);
    void InvalidateIssues(int projectId);
    void InvalidatePresence(string userId);
    void InvalidateUserProfile(string userId);
}

public enum CacheTier
{
    /// <summary>プレゼンスなど揮発性データ: 30秒</summary>
    Volatile  = 30,
    /// <summary>チケット一覧など短命データ: 2分</summary>
    Short     = 120,
    /// <summary>Wikiページツリーなど中間データ: 5分</summary>
    Medium    = 300,
    /// <summary>ADグループ情報など安定データ: 30分</summary>
    Stable    = 1800,
    /// <summary>ユーザープロファイル写真など長期データ: 6時間</summary>
    Long      = 21600
}

public class CacheService(
    IMemoryCache cache,
    ILogger<CacheService> logger) : ICacheService
{
    public T? Get<T>(string key) where T : class
    {
        cache.TryGetValue(key, out T? value);
        return value;
    }

    public void Set<T>(string key, T value, CacheTier tier) where T : class
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds((int)tier))
            // メモリ圧迫時の退避優先度
            .SetPriority(tier switch
            {
                CacheTier.Volatile => CacheItemPriority.Low,
                CacheTier.Short    => CacheItemPriority.Normal,
                CacheTier.Medium   => CacheItemPriority.Normal,
                CacheTier.Stable   => CacheItemPriority.High,
                CacheTier.Long     => CacheItemPriority.High,
                _                  => CacheItemPriority.Normal
            });

        cache.Set(key, value, options);
    }

    public void Remove(string key) => cache.Remove(key);

    public void RemoveByPrefix(string prefix)
    {
        // IMemoryCache にはプレフィックス削除 API がないため
        // キー追跡セットを使う（MemoryCache の実装依存を避けるため保守的に実装）
        if (cache is MemoryCache mc)
        {
            // .NET の MemoryCache は EnumerateKeys() を持たないため
            // アプリ起動時に登録したキー追跡 HashSet を使用
            lock (_trackedKeys)
            {
                var toRemove = _trackedKeys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var key in toRemove)
                {
                    mc.Remove(key);
                    _trackedKeys.Remove(key);
                }
            }
            logger.LogDebug("キャッシュ一括削除: prefix={Prefix}", prefix);
        }
    }

    // ── ドメイン固有の無効化 ─────────────────────────────────

    public void InvalidateWikiTree(int? parentId = null)
    {
        if (parentId.HasValue)
            Remove($"page_children_{parentId.Value}");
        else
            RemoveByPrefix("page_children_");
    }

    public void InvalidateIssues(int projectId)
        => RemoveByPrefix($"issues_{projectId}_");

    public void InvalidatePresence(string userId)
        => Remove($"presence:{userId}");

    public void InvalidateUserProfile(string userId)
    {
        Remove($"profile:{userId}");
        Remove($"photo:{userId}");
        Remove($"ad_profile_{userId}");
        Remove($"ad_groups_{userId}");
    }

    // キー追跡（プレフィックス削除のため）
    private static readonly HashSet<string> _trackedKeys = [];
}
