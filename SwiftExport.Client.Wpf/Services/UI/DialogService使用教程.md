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

关闭外层滚动不会为 Dialog 建立固定高度。如果调用方没有提供 `Height`，正文仍可能按数据量改变弹窗高度。表格类页面应明确设置合适的 `Width`、`Height`，并让内部 `DataGrid`、`ListBox` 在剩余空间内自行滚动：

```csharp
var options = new DialogOptions("选择数据")
{
    Width = 1200,
    Height = 760,
    MaxWidth = 1400,
    MaxHeight = 850,
    ScrollStrategy = DialogScrollStrategy.Content
};
```

WPF-UI 4.3.0 的 `DialogWidth`、`DialogHeight` 原生表示模板外壳的最大尺寸，布局时还会被正文期望尺寸改写。公共服务会为明确填写的 `Width`、`Height` 同步外壳最小尺寸，使它们表现为固定外壳尺寸；宿主变小时仍会收缩到可用范围。不填写时不施加最小尺寸，继续采用 WPF-UI 的按内容测量行为。

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

`Width`、`Height`、`MaxWidth`、`MaxHeight` 使用 WPF DIP。未设置 `Width` 或 `Height` 时，服务不写入本地值，保留 ContentDialog 的样式或控件默认值；同时仍通过 `DialogMaxWidth`、`DialogMaxHeight` 施加当前宿主边界。设置了首选尺寸时，服务会把它限制在请求最大值和当前 `ContentDialogHost.ActualWidth/ActualHeight` 内，并在宿主尺寸变化时重新计算。

标题、按钮和模板边距属于 Dialog 外壳。调用方应根据整个弹窗需要设置 `Height`，不在公共服务中手工扣除标题或按钮高度。WPF 使用 DIP，因此无需手工进行 DPI 像素换算。

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

## 9. 当前渐进迁移边界

现有少量 Feature service 和独立设置窗口仍直接依赖 WPF-UI `IContentDialogService`。它们在迁移前不受公共服务的并发保护和统一释放逻辑约束；本次公共基础设施不会自动改变这些业务流程。

独立窗口当前会把 Singleton `IContentDialogService` 切换到窗口本地宿主。后续迁移多窗口 Dialog 时，需要为本地宿主建立进入和恢复闭环；不要在业务 ViewModel 中保存或切换 `ContentDialogHost`。
