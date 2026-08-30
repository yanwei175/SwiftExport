# .NET 10 WPF 官方主题与项目样式规则

本文用于 `SwiftExport.Client.Wpf` 新 .NET 10 WPF 客户端的主题和样式设计。

当前源码事实：`SwiftExport.Client.Wpf` 已重新引用 lepoco `WPF-UI 4.3.0`，并使用 `ui:FluentWindow`、`ui:TitleBar`、`InfoBar`、`Snackbar`、`ContentDialog` 等 Shell 级能力。本文中“只使用官方 WPF Fluent 主题、不引入 WPF-UI”的旧判断已可能过期；后续以“WPF-UI 主题与 Shell 控件 + SwiftExport 命名业务样式”为准，但仍不恢复旧 `MateWpfThemes.FW`，也不覆盖默认基础控件隐式 Style。

核心原则：默认使用微软 WPF 官方主题和控件默认样式；SwiftExport 只在业务确实需要统一体验时，新增少量命名 Style 或专用控件，不覆盖 WPF 默认控件模板。

本文负责主题接入、`ThemeMode` 和 Style 边界；页面布局、间距、卡片、表单、DataGrid 页面结构见 [`.NET 10 WPF Fluent 布局规范`](wpf-fluent-layout-guide.md)。

来源：

- Microsoft Learn: https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/whats-new/net90
- Microsoft Learn: https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/whats-new/net100

## 1. 官方主题事实

.NET 9 开始，WPF 自带 Fluent 主题，目标是让 WPF 应用获得更现代的 Windows 11 视觉体验。官方提供：

- `ThemeMode="Light"`：浅色 Fluent。
- `ThemeMode="Dark"`：深色 Fluent。
- `ThemeMode="System"`：跟随 Windows 系统浅色 / 深色设置。
- `ThemeMode="None"`：默认值，使用旧 Aero2 主题。
- Fluent 资源字典：`pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml`。
- 系统强调色资源：`SystemColors.AccentColorBrushKey` 等，可通过 `DynamicResource` 跟随用户系统强调色变化。

.NET 10 继续改进 WPF 性能、Fluent 样式覆盖和 bug 修复；官方文档明确 Fluent UI 样式支持仍在进行中。因此 SwiftExport 不应自己大规模重写默认控件模板来“追现代”，而应让官方主题继续承接基础控件外观。

## 2. SwiftExport 默认策略

新客户端默认策略：

- `Application` 使用 `ThemeMode="System"`。
- 应用级资源可合并官方 Fluent 资源字典。
- 常规 `Button`、`TextBox`、`CheckBox`、`ComboBox`、`ListBox`、`ListView`、`TabControl` 等控件默认使用微软官方样式。
- 不为普通控件定义隐式全局样式，例如禁止新增无 `x:Key` 的 `<Style TargetType="{x:Type Button}">`。
- 不复制或重写官方控件模板，除非明确发现官方样式无法满足某个 ERP 业务控件需求。
- 项目资源只保留颜色、间距、字体、业务状态色和少量命名 Style。

推荐 `App.xaml` 结构：

```xml
<Application x:Class="SwiftExportClient.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ThemeMode="System">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml" />

                <!-- SwiftExport 只放 token 和命名样式，不覆盖默认控件样式。 -->
                <ResourceDictionary Source="Resources/Tokens.xaml" />
                <ResourceDictionary Source="Resources/Colors.xaml" />
                <ResourceDictionary Source="Resources/Typography.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

## 3. 项目样式边界

允许新增的项目资源：

- 设计 token：间距、圆角、字体尺寸、业务状态色。
- 命名 Style：例如 `SwiftExportPrimaryButton`、`SwiftExportDangerButton`、`SwiftExportShellMenuButton`。
- 业务控件样式：例如 ERP DataGrid、分页控件、状态栏、导航标签、搜索选择控件。
- 局部页面样式：只放在页面或模块资源内，不提升为全局默认。

禁止事项：

- 禁止覆盖所有 `Button`、`TextBox`、`ComboBox` 等基础控件默认样式。
- 禁止为追求统一视觉复制整套官方 `ControlTemplate`。
- 禁止在业务 ViewModel 中处理视觉样式。
- 禁止把单页面视觉修复提升成全局样式。
- 禁止引入第二套第三方主题库来接管官方控件外观。

如果确实需要统一某类业务按钮，使用命名 Style：

```xml
<Style x:Key="SwiftExportPrimaryButton"
       TargetType="{x:Type Button}"
       BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="MinWidth" Value="88" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
```

使用处显式引用：

```xml
<Button Content="保存"
        Style="{StaticResource SwiftExportPrimaryButton}" />
```

## 4. DataGrid 和业务控件

SwiftExport 可以自定义 DataGrid，因为 ERP 可编辑表格有明确业务需求：

- 行状态、脏数据、勾选、删除态、只读态需要稳定视觉表达。
- 批量编辑、批量保存、分页、图片列、查找列需要统一交互。
- 默认 DataGrid 外观不足以承载复杂业务状态。

DataGrid 自定义也应遵守边界：

- 优先自定义 `SwiftExportDataGrid` 命名 Style 或专用控件。
- 不影响普通 WPF `DataGrid` 的默认样式，除非整个客户端明确只使用 SwiftExport 业务 DataGrid。
- 行状态颜色使用项目 token，不在页面里散落硬编码颜色。
- DataGrid 行模型和集合模型仍按 `.NET 10 前端 MVVM 基础设计` 规划，不把视觉状态塞进 DTO。

## 5. 系统强调色和动态资源

涉及系统主题色时，优先使用微软提供的系统资源键，并使用 `DynamicResource`：

```xml
<TextBlock Text="当前模块"
           Foreground="{DynamicResource {x:Static SystemColors.AccentColorBrushKey}}" />
```

规则：

- 强调色用于选中态、焦点、当前菜单、当前标签等少量高语义位置。
- 不把整个应用主色硬编码成单一品牌色。
- 业务状态色仍使用项目 token，例如成功、警告、错误、只读、删除态。

## 6. 当前客户端清理状态

`SwiftExport.Client.Wpf` 已进入“官方 Fluent + 自建轻量资源”的方向，并已完成第一轮资源清理：

- `Button.xaml` 只保留命名 Style：`SwiftExportPrimaryButton`、`SwiftExportShellMenuButton`、`SwiftExportTabCloseButton`。
- 已移除空的 `TextBox.xaml` 合并资源，不再覆盖默认 `TextBox`。
- `DataGrid.xaml` 只保留命名 Style：`SwiftExportDataGrid`、`SwiftExportDataGridColumnHeader`、`SwiftExportDataGridCell`。
- `Typography.xaml` 中的 TextBlock 样式均为命名 Style，用于标题、正文、说明文字等项目文本语义。
- Shell 菜单和标签关闭按钮显式引用命名 Style；普通控件继续使用官方默认样式。

后续新增资源前先确认：这是项目级业务模式，还是单页面视觉修饰。

## 7. 检查清单

新增或修改 WPF 样式前检查：

- 是否能直接使用官方默认样式。
- 是否必须变成全局资源。
- 是否使用了 `x:Key`，避免隐式覆盖默认控件。
- 是否只影响 `SwiftExport.Client.Wpf`，不牵连旧 WPF / VSTO。
- 是否使用 `DynamicResource` 绑定可能随主题变化的颜色。
- 是否避免复制官方控件模板。
- 是否保留高对比度、系统主题和系统强调色的可用性。
