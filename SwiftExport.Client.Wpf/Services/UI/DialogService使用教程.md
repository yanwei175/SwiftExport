# DialogService 使用教程

`IDialogService` 是客户端展示 WPF-UI `ContentDialog` 的公共入口。业务 ViewModel 只依赖该接口和本目录中的公共类型，不引用 `Wpf.Ui.Controls.ContentDialog`，也不创建 View。

## 1. 简单消息和确认

已有调用方式保持不变：

```csharp
await _dialogs.ShowContentAsync("操作说明", "处理已经完成。");

var confirmed = await _dialogs.ConfirmAsync(
    "删除记录",
    "确定删除当前记录吗？",
    "删除",
    "返回",
    cancellationToken);
```

`ConfirmAsync` 只有在用户选择主按钮时返回 `true`。关闭按钮、Esc 和其他正常关闭方式返回 `false`。取消令牌会抛出 `OperationCanceledException`，不会伪装成用户取消。

## 2. 展示页面内容并获取明确结果

复杂内容使用 `ShowAsync`：

```csharp
var view = new CandidatePickerView
{
    DataContext = picker
};

var result = await _dialogs.ShowAsync(
    view,
    new DialogOptions("选择候选")
    {
        PrimaryButtonText = "确定",
        SecondaryButtonText = "应用并继续",
        CloseButtonText = "取消",
        DefaultButton = DialogButton.Primary,
        Width = 1200,
        MaxWidth = 1400,
        MaxHeight = 850,
        ScrollStrategy = DialogScrollStrategy.Content
    },
    dialogState,
    cancellationToken);

if (result == DialogResult.Primary)
{
    // 只在用户明确确认后接纳 picker 中的业务结果。
}
```

页面 View 由 Feature 的 UI service 创建并设置 `DataContext`。业务 ViewModel 不应 `new View`。如果应用资源已经为状态类型注册 `DataTemplate`，可以直接把状态对象作为 `content`。

## 3. 两种滚动策略

`DialogScrollStrategy.Dialog` 是默认值。它保留 WPF-UI 外层正文滚动，适合普通消息、说明文字和简单表单。

`DialogScrollStrategy.Content` 会把 ContentDialog 外层水平和垂直滚动设为 `Disabled`，适合内部含 `DataGrid`、`ListBox` 或复杂 Grid 的页面。内容页面必须具有有限布局，并在自己的列表控件上设置 `ScrollViewer.VerticalScrollBarVisibility="Auto"` 等滚动属性。

这里必须使用 `Disabled`。`Hidden` 只是隐藏滚动条，仍可能让外层 ScrollViewer 参与测量。

关闭外层滚动不会为 Dialog 建立固定高度。如果调用方没有提供高度或高度比例，正文仍可能按数据量改变弹窗高度。表格类页面可以按宿主可用空间的比例设置外壳，并让内部 `DataGrid`、`ListBox` 在剩余空间内自行滚动：

```csharp
var options = new DialogOptions("选择数据")
{
    WidthRatio = 0.95,
    HeightRatio = 0.9,
    ScrollStrategy = DialogScrollStrategy.Content
};
```

`WidthRatio = 1`、`HeightRatio = 1` 表示占满扣除 WPF-UI 外围留白后的宿主可用空间，并不表示屏幕全屏。比例会在宿主缩放时重新计算，所以大表格弹窗通常比写死 DIP 尺寸更合适。

WPF-UI 4.3.0 的 `DialogWidth`、`DialogHeight` 原生表示模板外壳的最大尺寸，布局时还会被正文期望尺寸改写。公共服务会为明确填写的固定尺寸或比例尺寸同步外壳最小、最大尺寸，使弹窗不会随列表行数变化，也不会由正文反向撑破宿主边界。不填写时不施加固定尺寸，继续采用 WPF-UI 的按内容测量行为。

外壳同步依赖当前 4.3.0 模板中的 `DialogRootGrid` 结构。升级 WPF-UI 时必须运行 `WpfUiDialogServiceTests` 的真实模板场景，确认模板名称、外围边距和标题/按钮布局仍兼容。

## 4. 动态按钮和关闭校验

需要动态按钮状态或关闭保护时，让页面状态或一个很薄的状态适配器实现 `IDialogContentState`。上例中的 `dialogState` 即该对象：

```csharp
public sealed class EditorDialogState : IDialogContentState
{
    public event EventHandler? StateChanged;

    public bool IsPrimaryButtonEnabled => HasValidInput && !IsWriting;
    public bool IsSecondaryButtonEnabled => !IsWriting;

    public bool CanClose(DialogResult requestedResult) => !IsWriting;

    private void NotifyDialogStateChanged() =>
        StateChanged?.Invoke(this, EventArgs.Empty);
}
```

`CanClose` 在 WPF-UI 的同步 `Closing` 事件中运行，因此必须快速完成，不能等待 API、打开另一个 Dialog 或执行写命令。需要异步确认的流程应在尝试关闭之前由 Feature 协调。

## 5. 业务生命周期

公共 DialogService 只管理 ContentDialog、宿主尺寸及自己的事件订阅。Feature service 继续负责业务状态的打开和关闭：

```csharp
await picker.OpenAsync(cancellationToken);
try
{
    return await _dialogs.ShowAsync(view, options, picker, cancellationToken);
}
finally
{
    picker.Close();
}
```

服务结束时会解除 `Closing`、状态变化和宿主尺寸事件，并清除 ContentDialog 对正文的引用；它不会擅自 `Dispose` 业务 ViewModel。

## 6. 尺寸和宿主

`Width`、`Height`、`MaxWidth`、`MaxHeight` 使用 WPF DIP；`WidthRatio`、`HeightRatio` 使用大于零且不超过一的有限数。固定尺寸和比例可以跨方向混用，例如固定宽度配比例高度；同一方向不能同时设置 `Width` 与 `WidthRatio`，或同时设置 `Height` 与 `HeightRatio`，否则服务会在展示前抛出参数异常。

未设置固定尺寸或比例时，服务不写入对应外壳尺寸，保留 ContentDialog 的按内容测量行为。`MaxWidth`、`MaxHeight` 始终是可选上限；最终外壳尺寸同时受固定值或比例值、显式最大值、当前宿主可用空间限制。

宿主可用空间按 `ContentDialogHost` 的实际布局尺寸计算，并扣除 WPF-UI 模板当前 `DialogMargin`。因此固定尺寸大于宿主时会安全收缩，比例一会保留模板外围留白。窗口缩小后再放大时，服务会从原始 `DialogOptions` 重新计算，而不是沿用缩小时的结果。

标题、正文和底部按钮都属于可见 Dialog 外壳。公共服务只扣模板外壳之外的 `DialogMargin`，不会手工扣标题或按钮高度，也不会重复扣正文边距。WPF 使用 DIP，因此无需手工进行 DPI 像素换算。

窗口必须在 `Loaded` 阶段通过 WPF-UI `IContentDialogService.SetDialogHost` 注册宿主。没有有效宿主时，公共服务会抛出明确异常。

## 7. 并发限制

一个 `WpfUiDialogService` 实例同一时间只允许一个由它管理的 Dialog。并发或嵌套调用会快速抛出 `InvalidOperationException`，不会排队；排队会让 Dialog 内部再次打开 Dialog 的代码形成自等待。

需要二次确认时，优先把确认步骤设计进当前页面状态或先结束当前 Dialog。不要在一个 ContentDialog 尚未结束时再调用公共 DialogService。

## 8. Feature service 的职责边界

Feature dialog service 可以负责：

- 创建对应 View 并绑定 ViewModel。
- 调用 ViewModel 的 `OpenAsync`、`Close` 或 `Dispose`。
- 依据 `DialogResult` 提取业务结果。
- 提供业务标题、按钮文字和合理尺寸。

公共 DialogService 负责：

- 创建 WPF-UI ContentDialog。
- 映射按钮和结果。
- 应用宿主尺寸与滚动策略。
- 同步按钮启用状态和关闭保护。
- 传递取消并释放公共事件订阅。

接口中不应增加采购、销售或其他业务模块专属方法。

## 9. 系统设置表单与异步主按钮

系统设置已进入 Shell 页签。每个页签创建独立的 `Features/SystemSettings/Services/SettingsDialogService.cs`，由它建立一次性表单、调用 Shell 的 `IDialogService.ShowAsync`、提取保存结果并在 `finally` 清理。`Features/SystemSettings/Forms/Views/SettingsFormContent.xaml` 承载正文与同一弹窗内的放弃提示；页签关闭时释放该服务。

需要在弹窗内提交并等待结果时，设置可选的 `PrimaryButtonCommand`：

```csharp
await dialogs.ShowAsync(view, new DialogOptions("用户资料")
{
    PrimaryButtonText = "保存资料",
    PrimaryButtonCommand = editor.SaveCommand,
    CloseButtonText = "关闭"
}, editor.CloseState);
```

该命令为 `IAsyncRelayCommand`。公共服务结合 `CanExecute`、`IsRunning` 和 `IDialogContentState` 控制主按钮，点击本身不关闭；业务 ViewModel 确认成功后通过 `IDialogCloseRequest` 请求关闭。校验或服务端业务失败保留输入，允许重试。未配置命令的弹窗仍按原方式返回按钮结果。未被业务层处理的异常会结束弹窗并传播给调用方，不会被静默吞掉。

正文不再重复放置保存按钮。UserControl 右上角关闭按钮绑定 `CloseState.CloseCommand`，不直接依赖 ContentDialog。`SettingsFormContent` 订阅关闭意图并显示放弃提示。Busy 时阻止普通关闭，放弃确认在当前弹窗内部完成；会话失效则强制关闭并清理敏感输入。

用户资料、用户角色、角色资料、角色权限分别位于 `RbacManagement/Users`、`RbacManagement/Roles` 下对应的用例目录；重置密码位于 `Passwords/Reset`。View 只负责控件、焦点和 PasswordBox 的读取清理，保存判断及关闭状态由 ViewModel 管理。个人修改密码位于 Passwords/Change，同样使用 Shell 公共 ContentDialog；成功后仍结束旧凭据会话。Shell 设置入口直接进入该工作区，默认个人概览，无管理权限也可使用个人功能。
