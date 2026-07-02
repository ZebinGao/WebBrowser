# WebBrowser

一款基于 **WPF + WebView2 + WPF-UI** 的个人轻量浏览器。刻意精简功能（不做广告拦截、不做扩展），把工程精力全部投在三个品质维度上：**极致 UI 响应、内存控制、Windows 原生融合感（Mica 毛玻璃 + 流畅拖拽 + 系统主题联动）**。

> 完整架构、关键决策、踩坑记录与验证结果见 [技术方案.md](技术方案.md)。

## 特性

- **多标签页**：自定义标签条并入标题栏（Mica 一体），favicon + 标题 + 关闭按钮，新建/选择/关闭。
- **内存控制**：
  - 关标签 → 该标签 renderer 进程**立即退出**（共享 browser/GPU/network 进程保留），连开连关零泄漏。
  - 后台标签 12s 后自动**挂起**（`TrySuspendAsync`），切回时即时恢复。
  - 工具栏实时遥测 `wv2: 进程数 · 内存 MB`。
- **自定义下载管理器**：接管 `DownloadStarting`、抑制默认下载条，文件落到真实 Downloads 目录（`SHGetKnownFolderPath`，支持重定位），带进度/速度/暂停/恢复/取消/打开/定位/重试的状态机面板。
- **Windows 原生视觉**：FluentWindow + Mica 背景 + 圆角 + 标题栏拖拽；启动跟随系统深浅主题 + 工具栏一键切换。
- **共享 WebView2 环境**：全应用单一 `CoreWebView2Environment`，所有标签复用同一进程树。
- **干净退出**：异步拆卸所有标签后再关窗，无残留 `msedgewebview2.exe` 进程。

## 技术栈

| | |
|---|---|
| 运行时 | .NET 8 (`net8.0-windows`) |
| Web 内核 | [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) 1.0.4022.49（Chromium Edge 内核） |
| UI 框架/控件 | [WPF-UI (lepoco)](https://github.com/lepoco/wpfui) 4.3.0 |
| MVVM | [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) 8.4.2（源生成器） |
| DI | `Microsoft.Extensions.DependencyInjection` |

## 生成与运行

前置：.NET 8 SDK、Windows 10/11（WebView2 运行时 Win11 预装）。

```bash
dotnet build
dotnet run
# 或在 Visual Studio 2022 中打开 WebBrowser.csproj 直接 F5
```

首次运行会在 `%LOCALAPPDATA%\WebBrowser\WebView2` 建立 WebView2 用户数据目录。

## 快捷键

- `Ctrl+T` 新建标签、`Ctrl+W` 关闭当前标签（需 chrome 有焦点时生效；页面内焦点转发见限制）
- 回车在地址栏：域名补 `https://`（如 `bing.com`），其余作为搜索

## 项目结构

```
App.xaml(.cs)                 启动 / DI / 系统主题 / 关闭编排
Helpers/                      AppPaths（路径）、UrlHelper（地址规范化）
Models/                       TabState、DownloadState 枚举
Services/                     WebViewEnvironmentService、TabLifecycleService、DownloadManagerService
WebView/WebViewTab.cs         持有单个 WebView2 + 事件订阅 + 挂起/恢复 + favicon
ViewModels/                   MainViewModel、TabViewModel、DownloadItemViewModel
Views/                        MainWindow、TabStripView、DownloadPanelView
```

详细职责与交互见 [技术方案.md](技术方案.md)。

## 已知限制

- 页面有焦点时，`Ctrl+T/W/L/Tab` 等快捷键未转发（WPF WebView2 不暴露 `AcceleratorKeyPressed`；需全局 keyboard hook）。
- `MemoryUsageTargetLevel=Low`（最小化时主动压内存）未接入。
- 无历史 / 收藏 / 多窗口 / 扩展（与精简定位一致）。
