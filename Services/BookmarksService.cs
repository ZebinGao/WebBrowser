using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WebBrowser.Helpers;
using WebBrowser.Models;

namespace WebBrowser.Services;

/// <summary>
/// 把用户书签持久化到 <see cref="AppPaths.BookmarksPath"/>（JSON）。扁平列表 —— 不含文件夹，刻意如此。
/// 持有一个供 UI 绑定的内存 <see cref="Items"/> 集合；变更会去抖刷盘。工具栏的星标按钮通过
/// <see cref="ContainsUrl"/> 来反映状态。
/// </summary>
public sealed class BookmarksService : IDisposable
{
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 注意：含 CJK/emoji 的标题会以 \uXXXX 转义存储，因为 System.Text.Json 的默认 encoder
        // 会转义非 ASCII 字符。反序列化时会还原成完全一致的原始文本，因此这只是文件可读性上的
        // 外观问题（此运行时投影下 UnsafeRelaxJsonEscaping 不可用）。
    };

    private readonly DispatcherTimer _flushTimer;
    private bool _dirty;
    private bool _disposed;

    public ObservableCollection<Bookmark> Items { get; } = new();

    public BookmarksService()
    {
        Load();

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FlushDelay };
        _flushTimer.Tick += (_, _) =>
        {
            _flushTimer.Stop();
            if (_dirty) Flush();
        };
    }

    /// <summary>当已存在精确匹配 URL 的书签时为 true（驱动星标按钮状态）。</summary>
    public bool ContainsUrl(string? url) => url is not null && Items.Any(b => b.Url == url);

    /// <summary>添加书签，除非已存在相同 URL 的书签。</summary>
    public void Add(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) || ContainsUrl(url))
            return;

        Items.Insert(0, new Bookmark
        {
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            AddedAt = DateTimeOffset.Now,
        });
        ScheduleFlush();
    }

    public void Remove(Bookmark bookmark)
    {
        if (Items.Remove(bookmark))
            ScheduleFlush();
    }

    /// <summary>URL 不存在则添加，存在则移除。返回该 URL 当前是否已被收藏。</summary>
    public bool Toggle(string url, string title)
    {
        var existing = Items.FirstOrDefault(b => b.Url == url);
        if (existing is not null)
        {
            Remove(existing);
            return false;
        }
        Add(url, title);
        return true;
    }

    private void ScheduleFlush()
    {
        _dirty = true;
        _flushTimer.Stop();
        _flushTimer.Start();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.BookmarksPath))
                return;
            var json = File.ReadAllText(AppPaths.BookmarksPath);
            var list = JsonSerializer.Deserialize<List<Bookmark>>(json, JsonOptions);
            if (list is null)
                return;
            foreach (var bookmark in list)
                Items.Add(bookmark);
        }
        catch
        {
            // 文件损坏或不可读 —— 从空白开始。
        }
    }

    private void Flush()
    {
        try
        {
            var json = JsonSerializer.Serialize(Items.ToList(), JsonOptions);
            var tmp = AppPaths.BookmarksPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(AppPaths.BookmarksPath))
                File.Replace(tmp, AppPaths.BookmarksPath, destinationBackupFileName: null);
            else
                File.Move(tmp, AppPaths.BookmarksPath);
            _dirty = false;
        }
        catch
        {
            // 持久化是尽力而为。
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _flushTimer.Stop();
        if (_dirty)
            Flush();
    }
}
