---
name: wpf-dotnet10-wpfui-controls
description: SwiftExport.Client.Wpf 的 lepoco/WPF-UI 控件库使用规则。用于 WPF-UI、Wpf.Ui、FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog、SymbolIcon、Navigation、第三方 Fluent 控件、WPF-UI 主题资源与官方 WPF Fluent 主题协作相关任务。
---

# WPF-UI Controls Skill

## 必读

- `SwiftExport.Client.Wpf/SwiftExport.Client.Wpf.csproj`
- `SwiftExport.Client.Wpf/App.xaml`
- `SwiftExport.Client.Wpf/Shell/ShellWindow.xaml`
- `SwiftExport.Client.Wpf/Services/UI/WpfUiDialogService.cs`
- `SwiftExport.Client.Wpf/Services/UI/WpfUiMessageService.cs`
- 涉及主题时读 `../official-theme/SKILL.md`
- 涉及布局时读 `../fluent-layout/SKILL.md`
- 涉及自定义控件时读 `../custom-controls/SKILL.md`

## 什么时候使用

- 使用或修改 WPF-UI 控件。
- 修改 `FluentWindow`、`TitleBar`、`InfoBar`、`Snackbar`、`ContentDialog`、`SymbolIcon`。
- 修改 WPF-UI 主题字典、控件字典或 XAML 命名空间。
- 设计第三方 Fluent 控件与项目资源的协作方式。
- 排查 WPF-UI Dialog、Snackbar、Navigation、Icon 相关问题。

## 当前项目事实

- 项目已引用 `WPF-UI` 4.3.0。
- XAML 命名空间：`xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`。
- `App.xaml` 已合并：
  - `<ui:ThemesDictionary Theme="Light" />`
  - `<ui:ControlsDictionary />`
  - 官方 `PresentationFramework.Fluent` 主题
  - SwiftExport 自有 Tokens、Colors、Typography、Spacing、Button、ShellNavigation、DataGrid
- Shell 使用 `ui:FluentWindow`、`ui:TitleBar`、`ui:InfoBar`、`ui:SymbolIcon`、`ui:ContentDialogHost`、`ui:SnackbarPresenter`。
- Dialog 和消息通过 `WpfUiDialogService`、`WpfUiMessageService` 封装。

## 核心规则

- WPF-UI 是当前 .NET 10 WPF 主线可用的第三方 Fluent 控件库。
- WPF-UI 与微软官方 WPF Fluent 主题共同作为视觉基础。
- 页面可以使用 WPF-UI 控件，但业务样式仍优先走 SwiftExport 命名资源和 token。
- ViewModel 不直接引用 WPF-UI 控件类型。
- Dialog、Snackbar、Message 通过项目 UI service 调用，不在 ViewModel 中直接 new `ContentDialog` 或操作 Presenter。
- 图标优先使用 WPF-UI `SymbolIcon` / `SymbolRegular`，除非项目已有明确图标资源。
- 不把 WPF-UI 示例页面结构照搬成 ERP 页面，ERP 页面仍优先信息密度和业务操作效率。

## 禁止事项

- 禁止在 ViewModel 中直接依赖 `Wpf.Ui.Controls.ContentDialog`、`SnackbarPresenter`、`Window`。
- 禁止绕过 `IDialogService` / `IMessageService` 直接操作全局 UI 宿主。
- 禁止为了单个页面修改 WPF-UI 全局主题字典。
- 禁止混用旧 MateWpfThemes 主题作为当前客户端基础。
- 禁止把 WPF-UI 控件当成业务逻辑容器。

## 详细参考

- `references/WPFUI控件规则.md`
