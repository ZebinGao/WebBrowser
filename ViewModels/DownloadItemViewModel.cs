using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using WebBrowser.Models;

namespace WebBrowser.ViewModels;

/// <summary>
/// 单个下载的 view model + 状态机。包装 <see cref="CoreWebView2DownloadOperation"/>：
/// 进度/速度来自 <c>BytesReceivedChanged</c>，生命周期来自 <c>StateChanged</c>（二者都在
/// worker 线程触发，故每次更新都转发到 UI 线程）。Paused/Cancelled 是叠加在 operation 的
/// InProgress/Interrupted/Completed 之上的本地 UI 状态。
/// </summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    private readonly CoreWebView2DownloadOperation _operation;
    private long _lastBytes;
    private DateTime _lastSample;
    private bool _isPaused;
    private bool _cancelled;

    public string FullPath { get; }
    public string FileName => Path.GetFileName(FullPath);

    public string SourceUri => _operation.Uri ?? string.Empty;

    [ObservableProperty] private DownloadState _state = DownloadState.InProgress;
    [ObservableProperty] private long _bytesReceived;
    [ObservableProperty] private long _totalBytesToReceive;
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private string _interruptReasonText = "";

    public double Progress => TotalBytesToReceive > 0
        ? Math.Min(1.0, (double)BytesReceived / TotalBytesToReceive)
        : 0;

    public bool IsIndeterminate => TotalBytesToReceive <= 0;
    public bool CanPause => State == DownloadState.InProgress;
    public bool CanResume => State == DownloadState.Paused;
    public bool CanCancel => State == DownloadState.InProgress || State == DownloadState.Paused;
    public bool CanOpen => State == DownloadState.Completed;
    public bool CanRetry => State == DownloadState.Interrupted || State == DownloadState.Cancelled;

    /// <summary>用户请求重试时触发；载荷为源 URL。</summary>
    public event Action<Uri>? RetryRequested;

    public DownloadItemViewModel(CoreWebView2DownloadOperation operation, string fullPath)
    {
        _operation = operation;
        FullPath = fullPath;

        BytesReceived = ToInt64(operation.BytesReceived);
        TotalBytesToReceive = ToInt64(operation.TotalBytesToReceive);
        State = MapState(operation.State);

        _operation.BytesReceivedChanged += OnBytesReceivedChanged;
        _operation.StateChanged += OnStateChanged;
    }

    private void OnBytesReceivedChanged(object? sender, object e) => Marshal(RefreshProgress);
    private void OnStateChanged(object? sender, object e) => Marshal(RefreshState);

    private void RefreshProgress()
    {
        long received = ToInt64(_operation.BytesReceived);
        long total = ToInt64(_operation.TotalBytesToReceive);
        long delta = received - _lastBytes;
        DateTime now = DateTime.UtcNow;
        double seconds = (now - _lastSample).TotalSeconds;

        if (_lastSample != default && seconds > 0 && delta >= 0)
            SpeedText = FormatSpeed(delta / seconds);

        _lastBytes = received;
        _lastSample = now;
        BytesReceived = received;

        bool sizeChanged = TotalBytesToReceive != total;
        TotalBytesToReceive = total;
        OnPropertyChanged(nameof(Progress));
        if (sizeChanged)
            OnPropertyChanged(nameof(IsIndeterminate));
    }

    private void RefreshState()
    {
        if (_cancelled)
            State = DownloadState.Cancelled;
        else
            State = _isPaused ? DownloadState.Paused : MapState(_operation.State);

        if (_operation.State == CoreWebView2DownloadState.Interrupted)
            InterruptReasonText = _operation.InterruptReason.ToString();

        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(Progress));
    }

    /// <summary>
    /// 稳健地将下载 operation 的字节计数转换为 <see cref="long"/>。<c>BytesReceived</c>/<c>TotalBytesToReceive</c>
    /// 的确切类型在 WinRT projection 与 COM interop wrapper 之间不一致（long / ulong / ulong?）；
    /// boxing + <see cref="Convert"/> 可统一处理，null（总大小未知）转为 0。
    /// </summary>
    private static long ToInt64(object? value) =>
        value is null ? 0 : ((IConvertible)value).ToInt64(System.Globalization.CultureInfo.InvariantCulture);

    private static DownloadState MapState(CoreWebView2DownloadState state) => state switch
    {
        CoreWebView2DownloadState.InProgress => DownloadState.InProgress,
        CoreWebView2DownloadState.Interrupted => DownloadState.Interrupted,
        CoreWebView2DownloadState.Completed => DownloadState.Completed,
        _ => DownloadState.InProgress,
    };

    private static string FormatSpeed(double bytesPerSecond)
    {
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        double value = bytesPerSecond;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:F1} {units[i]}";
    }

    private static void Marshal(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
            dispatcher.BeginInvoke(action);
        else
            action();
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelled = true;
        try { _operation.Cancel(); }
        catch { /* 忽略 */ }
        State = DownloadState.Cancelled;
        RaiseCommandStates();
    }

    [RelayCommand]
    private void Pause()
    {
        try { _operation.Pause(); _isPaused = true; State = DownloadState.Paused; }
        catch { /* 并非所有下载类型都支持 Pause —— 忽略 */ }
        RaiseCommandStates();
    }

    [RelayCommand]
    private void Resume()
    {
        try { _operation.Resume(); _isPaused = false; State = DownloadState.InProgress; }
        catch { /* 忽略 */ }
        RaiseCommandStates();
    }

    [RelayCommand]
    private void Open()
    {
        try { Process.Start(new ProcessStartInfo(FullPath) { UseShellExecute = true }); }
        catch { /* 忽略 */ }
    }

    [RelayCommand]
    private void Reveal()
    {
        try { Process.Start("explorer.exe", $"/select,\"{FullPath}\""); }
        catch { /* 忽略 */ }
    }

    [RelayCommand]
    private void Retry()
    {
        if (Uri.TryCreate(SourceUri, UriKind.Absolute, out var uri))
            RetryRequested?.Invoke(uri);
    }
}
