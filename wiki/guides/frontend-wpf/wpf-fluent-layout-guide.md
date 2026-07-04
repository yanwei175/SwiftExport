# .NET 10 WPF Fluent 布局规范

本文用于 `SwiftExport.Client.Wpf` 新 .NET 10 WPF 客户端的页面布局、间距、表单、DataGrid 页面和 XAML 编写规范。

来源：`raw/inbox/Fluent  Microsoft 风格布局规范.md`

项目化口径：

当前源码事实：`SwiftExport.Client.Wpf` 已重新使用 lepoco `WPF-UI 4.3.0` 承载 Shell 级 FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog 和左侧导航外观。本文中“不再默认引入 WPF-UI 控件库”的旧判断已可能过期；布局规范仍继续适用，尤其是 4px 间距、页面 Header、查询区、工具栏、DataGrid、状态/分页区分层。

- 默认使用微软官方 WPF Fluent 主题和原生 WPF 控件。
- 不再默认引入 WPF-UI 控件库。
- 普通控件不覆盖官方默认样式；特殊业务体验通过命名 Style 或专用控件实现。
- 复杂业务控件，例如 DataGrid Lookup、ProductPicker、SearchableTextBox、分页控件，可以自定义。
- 本文管布局和视觉层级；主题接入、`ThemeMode`、命名 Style 边界见 [`.NET 10 WPF 官方主题与项目样式规则`](wpf-official-theme-style-guide.md)。

## 1. 设计目标

新客户端界面应接近 Microsoft Fluent / Windows 11 风格，而不是传统 WinForms / 老式 WPF 风格。

目标：

- 留白充足，界面不要拥挤。
- 表单区、卡片区、列表区有清晰层级。
- 控件之间的距离统一，不随意写 `Margin`。
- 优先使用 4px 倍数间距系统。
- 页面像现代 Web 后台或 Win11 应用。
- 表单和按钮对齐，输入区有明确宽度。
- DataGrid 页面清晰区分查询区、工具栏、表格和状态栏。

## 2. 间距系统

所有 `Margin`、`Padding`、布局间隔优先使用以下值：

- `4`：极小间距、紧凑控件内部微调、图标与文字之间的小间距。
- `8`：控件与 Label、同组相邻元素、按钮图标与文字之间的距离。
- `12`：Label 与输入控件、说明文字、紧凑字段、文本段落之间的小间距。
- `16`：卡片内部 Padding、列表项 Padding、普通控件组、表单行之间的标准距离。
- `24`：页面内容区块、表单与按钮区、工具栏与 DataGrid、主要内容分区之间。
- `32`：页面主容器 Padding、Dialog / Form 左右内边距、页面顶部和内容之间的主间距。
- `48`：大区块、页面主标题与下一个重要内容区、重要 Section 之间。

禁止随意使用 `5`、`7`、`9`、`13`、`17`、`23` 这类非标准间距。确需视觉微调时，应优先局部处理，并避免提升为全局规则。

推荐资源放在 `SwiftExport.Client.Wpf/Resources/Spacing.xaml`：

```xml
<sys:Double x:Key="Space4">4</sys:Double>
<sys:Double x:Key="Space8">8</sys:Double>
<sys:Double x:Key="Space12">12</sys:Double>
<sys:Double x:Key="Space16">16</sys:Double>
<sys:Double x:Key="Space24">24</sys:Double>
<sys:Double x:Key="Space32">32</sys:Double>
<sys:Double x:Key="Space48">48</sys:Double>

<Thickness x:Key="PagePadding">32</Thickness>
<Thickness x:Key="CardPadding">16</Thickness>
<Thickness x:Key="SectionMargin">0,0,0,24</Thickness>
<Thickness x:Key="FieldMargin">0,0,0,16</Thickness>
<Thickness x:Key="CompactFieldMargin">0,0,0,12</Thickness>
<Thickness x:Key="ButtonGroupMargin">0,24,0,0</Thickness>
```

如果项目已有 `Tokens.xaml` / `Spacing.xaml`，优先合并到现有结构，不重复创建语义相近的资源。

## 3. 页面布局

普通页面结构：

```text
Page
  -> Root Grid / ScrollViewer
    -> Main Container
      -> Page Header
      -> Description / Tip
      -> Content Section
      -> Footer / Action Area
```

规则：

- 普通页面内容 Padding：`32`。
- 紧凑弹窗内容 Padding：`24`。
- 大页面 Section 之间：`24`。
- 带标题的大区块之间：`48`。
- 页面标题与副标题 / 说明之间：`8` 到 `12`。
- 页面标题与主要内容之间：`24` 或 `48`，按页面复杂度决定。

示例结构：

```xml
<Grid Margin="{StaticResource PagePadding}">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="{StaticResource Space24}" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <StackPanel Grid.Row="0">
        <TextBlock Text="页面标题" Style="{StaticResource SwiftExportTitleText}" />
        <TextBlock Text="页面说明文字"
                   Style="{StaticResource SwiftExportCaptionText}"
                   Margin="0,8,0,0" />
    </StackPanel>

    <Grid Grid.Row="2">
        <!-- 页面主要内容 -->
    </Grid>
</Grid>
```

## 4. 卡片布局

卡片适合用于：

- 设置项。
- 统计区块。
- 登录框。
- 表单容器。
- 功能入口。
- 信息展示。

规则：

- 卡片内部 Padding：`16`。
- 卡片之间距离：`16` 或 `24`。
- 卡片标题与内容之间：`12`。
- 卡片内操作按钮放在底部，距离内容 `16`。
- 卡片内图片 / 预览区域与文字之间：`16`。
- 卡片不要过度嵌套。

卡片 Style 必须使用命名 Style，例如 `SwiftExportCardBorder`，不要隐式覆盖所有 `Border`。

## 5. 表单布局

表单必须清晰、有序，不能把所有控件挤在一起。

规则：

- 表单容器 Padding：`24` 或 `32`。
- Label 与输入控件之间：`8`。
- 字段与字段之间：`16`。
- 相关字段紧凑排列时可用 `12`。
- 表单 Section 之间：`24`。
- 底部按钮区与表单内容之间：`24`。
- 按钮之间距离：`8`。
- 输入框宽度要统一，不要忽长忽短。
- 两列表单列间距推荐 `24`。
- 复杂表单优先用 `Grid`，简单纵向内容可用 `StackPanel`。

字段布局：

```xml
<StackPanel Margin="{StaticResource FieldMargin}">
    <TextBlock Text="用户名" Margin="0,0,0,8" />
    <TextBox Text="{Binding UserName, UpdateSourceTrigger=PropertyChanged}" />
</StackPanel>
```

## 6. 按钮布局

规则：

- 主按钮使用命名 Style，例如 `SwiftExportPrimaryButton`。
- 次按钮优先使用微软官方默认样式。
- 危险操作必须使用 Danger 风格或明确红色提示，不得和普通按钮一样。
- 普通表单按钮 `MinWidth` 推荐 `96` 到 `120`。
- 登录页、确认页大按钮高度可用 `44` 到 `52`。
- 按钮之间间距统一 `8`。
- 按钮组与上方内容距离 `24`。

按钮顺序：

- 保存 / 确认 / 登录：Primary。
- 取消 / 返回：默认或 Secondary。
- 删除 / 作废：Danger。

## 7. DataGrid 页面布局

业务 DataGrid 页面必须分区：

1. 页面标题区。
2. 查询筛选区。
3. 工具栏区。
4. DataGrid 主表格区。
5. 状态 / 分页 / 汇总区。

推荐结构：

```text
Header
24px
Search Panel
16px
Toolbar
12px
DataGrid
12px
Footer / Pagination / Summary
```

规则：

- DataGrid 不要紧贴页面边缘。
- 查询区和 DataGrid 之间至少 `16`。
- 工具栏和 DataGrid 之间 `12`。
- DataGrid 行高统一。
- 单元格 Padding 推荐横向 `8`，纵向 `4` 到 `6`。
- 表头清晰，文字垂直居中。
- 可编辑单元格的编辑态和显示态要区分。
- 复杂选择列不要直接塞第三方 AutoSuggestBox。
- 单元格编辑选择优先使用自定义 LookupBox / DataGridLookupColumn。
- DataGrid 自定义样式使用 `SwiftExportDataGrid` 这类命名 Style，不隐式覆盖默认 `DataGrid`。

## 8. Dialog / 弹窗布局

规则：

- 内容 Padding：`24`。
- 标题与说明之间：`8`。
- 说明与主体内容之间：`24`。
- 表单字段之间：`16`。
- 底部按钮区与内容之间：`24`。
- 按钮间距：`8`。
- 普通表单 Dialog 宽度推荐 `480` 到 `720`。

结构：

```text
Title
8px
Description
24px
Form Content
24px
Action Buttons
```

ViewModel 不直接 `MessageBox.Show` 或 `new Window()`；阻塞确认、错误详情、文件选择等交互应走后续统一 `IDialogService`。

## 9. 登录页布局

登录页要简洁，不要像传统表单。

规则：

- 登录卡片宽度 `420` 到 `520`。
- 页面整体居中。
- 系统名与副标题之间：`8`。
- 标题区与表单区之间：`32`。
- 字段之间：`16` 或 `24`。
- 登录按钮与密码框之间：`24` 或 `32`。
- 登录按钮高度：`44` 到 `52`。
- 错误提示放在按钮下方或表单底部，距离 `12`。

## 10. Typography

文字层级建议：

- `PageTitle`：`28` 到 `36`，Bold / SemiBold。
- `SectionTitle`：`20` 到 `24`，SemiBold。
- `CardTitle`：`16` 到 `18`，SemiBold。
- `Body`：`13` 到 `14`，Regular。
- `Caption`：`12`，Regular，弱化灰色。
- `Label`：`13` 到 `14`，SemiBold 或 Medium。
- `Error`：`12` 到 `13`，红色。

规则：

- 页面只能有一个最强标题。
- Section 标题不要比 PageTitle 抢眼。
- Label 不要过大。
- 说明文字用弱化灰色。
- 错误提示用红色，但不要大面积使用。

## 11. 颜色与状态

颜色规则：

- 主色优先使用系统 Accent 或项目 Primary token。
- 普通文本使用高对比深色。
- 说明文字使用弱化灰色。
- 边框使用浅灰。
- 错误使用 Danger 色。
- 成功使用 Success 色。
- 警告使用 Warning 色。
- 同一页面不要使用太多高饱和颜色。

状态规则：

- Hover 要有轻微变化。
- Pressed 要比 Hover 更深。
- Disabled 要降低透明度或使用弱灰。
- Focus 要有清晰边框。
- Error 要有红色边框或提示文字。

## 12. 控件选择

默认选择：

- 普通页面按钮、输入框、复选框、下拉框、列表、Tab 使用 WPF 官方默认控件样式。
- 需要统一业务语义时使用命名 Style。
- DataGrid 使用 WPF 原生 `DataGrid` 加 SwiftExport 命名 Style / 专用列。
- DataGrid 单元格内避免堆复杂第三方控件。
- 页面顶部搜索可以使用普通 `TextBox` + 命令，后续如需要再做专用搜索控件。

允许自定义：

- DataGrid Lookup。
- ProductPicker。
- SearchableTextBox。
- 分页控件。
- 状态栏和导航标签。

## 13. XAML 编写规则

- 不在 XAML 中大量硬编码 `Margin`。
- 优先使用资源：`PagePadding`、`CardPadding`、`FieldMargin`、`SectionMargin`、`Space8`、`Space12`、`Space16`、`Space24`、`Space32`。
- `Grid` 比 `StackPanel` 更适合复杂表单。
- 多列对齐表单必须用 `Grid`。
- 控件命名要清晰。
- 可复用 Style 集中到 `Resources/`，使用 `x:Key` 命名。
- 页面内临时样式可以局部写，但后续重复出现时再提取成资源。
- Binding 包含必要的 `UpdateSourceTrigger`。
- 表单验证保留 `ValidatesOnNotifyDataErrors` / `NotifyOnValidationError`。
- 不为了视觉效果破坏 MVVM 绑定。
- 不把复杂业务逻辑写进 code-behind。

## 14. 输出要求

当 Codex 设计或重构界面时，应做到：

- 先说明采用的布局结构。
- 如需新增资源，说明放入哪个 `ResourceDictionary`。
- 如果发现现有 Margin 混乱，逐步替换为 spacing token。
- 不为了好看破坏 MVVM 绑定。
- 不把复杂业务逻辑写到 code-behind。
- 不把 DataGrid 的复杂编辑行为交给普通第三方控件硬撑。
- 保持界面现代、清爽、留白合理。
