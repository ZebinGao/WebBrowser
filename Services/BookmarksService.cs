using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WebBrowser.Helpers;
using WebBrowser.Models;

namespace WebBrowser.Services;

/// <summary>
/// Persists the user's bookmarks to <see cref="AppPaths.BookmarksPath"/> (JSON). Flat list — no folders,
/// by design. Holds an in-memory <see cref="Items"/> collection the UI binds to; mutations are
/// debounced to disk. The toolbar star button reads <see cref="ContainsUrl"/> to reflect state.
/// </summary>
public sealed class BookmarksService : IDisposable
{
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

    /// <summary>True if a bookmark with the exact URL already exists (drives the star button state).</summary>
    public bool ContainsUrl(string? url) => url is not null && Items.Any(b => b.Url == url);

    /// <summary>Adds a bookmark unless one with the same URL already exists.</summary>
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

    /// <summary>Adds the URL if absent, removes it if present. Returns true if it is now bookmarked.</summary>
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
            // Corrupt or unreadable file — start fresh.
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
            // Best-effort persistence.
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
