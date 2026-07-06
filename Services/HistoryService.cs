using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WebBrowser.Helpers;
using WebBrowser.Models;

namespace WebBrowser.Services;

/// <summary>
/// 把浏览历史持久化到 <see cref="AppPaths.HistoryPath"/>（JSON）。持有一个供 UI 绑定的内存
/// <see cref="Entries"/> 集合（最新的在最前）；变更会去抖刷盘，避免快速导航时频繁打 IO。
/// 条目按 URL 去重（visit count + last-visited 上调，并移到最前），且限制在 <see cref="MaxEntries"/> 以内。
/// </summary>
public sealed class HistoryService : IDisposable
{
    /// <summary>硬性上限，使 history.json 不会无限增长。</summary>
    private const int MaxEntries = 1000;

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

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public HistoryService()
    {
        Load();

        // 去抖：每次变更都重置计时器，待导航平息后刷一次盘。
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FlushDelay };
        _flushTimer.Tick += (_, _) =>
        {
            _flushTimer.Stop();
            if (_dirty) Flush();
        };
    }

    /// <summary>记录一次成功的导航。重复的 URL 会增加 VisitCount 并把该条目移到最前。</summary>
    public void Record(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var existing = Entries.FirstOrDefault(e => e.Url == url);
        if (existing is not null)
        {
            existing.VisitCount++;
            existing.LastVisited = DateTimeOffset.Now;
            if (!string.IsNullOrWhiteSpace(title))
                existing.Title = title;

            int idx = Entries.IndexOf(existing);
            if (idx > 0)
            {
                Entries.RemoveAt(idx);
                Entries.Insert(0, existing);
            }
        }
        else
        {
            Entries.Insert(0, new HistoryEntry
            {
                Url = url,
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                LastVisited = DateTimeOffset.Now,
                VisitCount = 1,
            });

            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        }

        ScheduleFlush();
    }

    public void Remove(HistoryEntry entry)
    {
        if (Entries.Remove(entry))
            ScheduleFlush();
    }

    public void Clear()
    {
        if (Entries.Count == 0)
            return;
        Entries.Clear();
        ScheduleFlush();
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
            if (!File.Exists(AppPaths.HistoryPath))
                return;
            var json = File.ReadAllText(AppPaths.HistoryPath);
            var list = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions);
            if (list is null)
                return;
            foreach (var entry in list)
                Entries.Add(entry);
        }
        catch
        {
            // 文件损坏或不可读 —— 从空白开始，避免启动时崩溃。
        }
    }

    /// <summary>把集合原子地写入磁盘（临时文件，再 replace/move）。</summary>
    private void Flush()
    {
        try
        {
            var json = JsonSerializer.Serialize(Entries.ToList(), JsonOptions);
            var tmp = AppPaths.HistoryPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(AppPaths.HistoryPath))
                File.Replace(tmp, AppPaths.HistoryPath, destinationBackupFileName: null);
            else
                File.Move(tmp, AppPaths.HistoryPath);
            _dirty = false;
        }
        catch
        {
            // 持久化是尽力而为；绝不因写历史而让浏览器崩溃。
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
