using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WebBrowser.Helpers;
using WebBrowser.Models;

namespace WebBrowser.Services;

/// <summary>
/// Persists browsing history to <see cref="AppPaths.HistoryPath"/> (JSON). Holds an in-memory
/// <see cref="Entries"/> collection (newest first) that the UI binds to; mutations are debounced to
/// disk so rapid navigations don't thrash IO. Entries are deduped by URL (visit count + last-visited
/// bump, moved to top) and capped at <see cref="MaxEntries"/>.
/// </summary>
public sealed class HistoryService : IDisposable
{
    /// <summary>Hard cap so history.json never grows unbounded.</summary>
    private const int MaxEntries = 1000;

    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // NOTE: titles with CJK/emoji are stored as \uXXXX escapes because System.Text.Json's default
        // encoder escapes non-ASCII. They deserialize back to the exact original text, so this is purely
        // a file-readability cosmetic (UnsafeRelaxJsonEscaping isn't available in this runtime projection).
    };

    private readonly DispatcherTimer _flushTimer;
    private bool _dirty;
    private bool _disposed;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public HistoryService()
    {
        Load();

        // Debounce: reset the timer on every mutation, flush once navigation settles.
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FlushDelay };
        _flushTimer.Tick += (_, _) =>
        {
            _flushTimer.Stop();
            if (_dirty) Flush();
        };
    }

    /// <summary>Records a successful navigation. A repeat URL bumps VisitCount and moves the entry to the top.</summary>
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
            // Corrupt or unreadable file — start fresh rather than crash on startup.
        }
    }

    /// <summary>Writes the collection to disk atomically (temp file, then replace/move).</summary>
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
            // Persistence is best-effort; never crash the browser over a history write.
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
