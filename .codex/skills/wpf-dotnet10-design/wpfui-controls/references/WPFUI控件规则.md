# WPF-UI 控件规则

## 依赖

`SwiftExport.Client.Wpf/SwiftExport.Client.Wpf.csproj`：

```xml
<PackageReference Include="WPF-UI" Version="4.3.0" />
```

常用命名空间：

```csharp
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
```

XAML：

```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```

## 主题顺序

`App.xaml` 当前主题资源包含：

```xml
<ui:ThemesDictionary Theme="Light" />
<ui:ControlsDictionary />
<ResourceDictionary Source="pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml" />
<ResourceDictionary Source="Resources/Tokens.xaml" />
<ResourceDictionary Source="Resources/Colors.xaml" />
<ResourceDictionary Source="Resources/Typography.xaml" />
<ResourceDictionary Source="Resources/Spacing.xaml" />
<ResourceDictionary Source="Resources/Button.xaml" />
<ResourceDictionary Source="Resources/ShellNavigation.xaml" />
<ResourceDictionary Source="Resources/DataGrid.xaml" />
```

规则：

- WPF-UI 提供第三方 Fluent 控件和默认控件资源。
- 官方 WPF Fluent 主题提供基础 Fluent 外观。
- SwiftExport 资源负责业务 token、命名 Style 和页面补充。
- 修改全局资源前必须搜索影响范围。

## 推荐使用场景

适合使用 WPF-UI：

- Shell 窗口：`FluentWindow`。
- 标题栏：`TitleBar`。
- 全局提示：`InfoBar`。
- 消息提示：`Snackbar`。
- 对话框：`ContentDialog`。
- Fluent 图标：`SymbolIcon`。
- 需要第三方 Fluent 控件补齐官方 WPF 控件不足时。

优先使用原生 WPF 或项目控件：

- 高密度 ERP 表格。
- 复杂 DataGrid 编辑。
- 强业务语义的选择器、搜索框。
- 需要严格控制绑定和变更追踪的控件。

## Dialog / Message

项目已有封装：

- `WpfUiDialogService`
- `WpfUiMessageService`

ViewModel 应依赖项目抽象接口，例如：

- `IDialogService`
- `IMessageService`

不要在 ViewModel 中直接依赖 `IContentDialogService`、`ISnackbarService`，除非正在实现 UI service 本身。

### WPF-UI 4.3.0 已知问题

WPF-UI 4.3.0 的 `ui:Button` 按下状态模板会把 `Foreground` 绑定到
`RelativeSource TemplatedParent.PressedForeground`。当按钮位于 `ContentDialog` 模板内时，
TemplatedParent 是 `ContentDialog`，调试输出会报告找不到 `PressedForeground`。该诊断不影响按钮命令执行。

- 上游模板：https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui/Controls/Button/Button.xaml
- 上游修复：https://github.com/lepoco/wpfui/pull/1723

当前不在客户端复制 WPF-UI 完整 Button 模板。后续稳定版包含修复后升级依赖，并删除本段兼容说明。

## 图标

Shell 当前使用 `ui:SymbolIcon` 和 `SymbolRegular`。

规则：

- 常规按钮和导航图标优先用 WPF-UI 图标。
- 业务特定图标可后续建立项目图标映射。
- 不手写复杂 SVG 图标塞进按钮，除非 WPF-UI 没有合适图标。

## 与自定义控件的关系

- WPF-UI 控件可以作为自定义控件的视觉基础。
- 自定义控件 API 仍使用 WPF 依赖属性、Binding、Command、Event。
- 控件内部不调用 API，不访问 Repository，不处理业务流程。
