# 官方 MVVM Toolkit 示例参考

本文件提炼自 `D:/Documents/DavidWiki/raw/inbox/mvvm-toolkit-samples/references/` 中的 Microsoft MVVM Toolkit Sample App 资料。用途是当 AI 不确定 CommunityToolkit.Mvvm 的官方写法时，先在这里确认模式，再结合 SwiftExport 当前项目规则实现代码。

## 使用原则

- SwiftExport 业务 ViewModel、LineModel、SearchModel 默认使用源生成器：`[ObservableProperty]`、`[RelayCommand]`、`[NotifyCanExecuteChangedFor]`、`[NotifyPropertyChangedFor]`。
- 官方 sample 中大量手写 `SetProperty`、`new RelayCommand`、`new AsyncRelayCommand` 的示例只作为理解底层 API，不作为 SwiftExport 新业务代码首选。
- 如果官方示例与 SwiftExport 的 `communitytoolkit-mvvm/SKILL.md` 冲突，以 SwiftExport skill 为准。
- 写 WPF 代码时使用 `Binding`，不要照搬 WinUI 的 `x:Bind`。

## 参考文件地图

源资料目录：

```text
D:/Documents/DavidWiki/raw/inbox/mvvm-toolkit-samples/references/
```

按问题选择：

- `Introduction.md`：MVVM Toolkit 总览、包用途。
- `ObservableObject.md`：`ObservableObject`、`SetProperty`、依赖通知、包装非可观察模型、Task 通知。
- `ObservableRecipient.md`：激活状态、Messenger 集成。
- `ObservableValidator.md`：`INotifyDataErrorInfo`、DataAnnotations、自定义验证。
- `RelayCommand.md`：同步命令、`CanExecute`、`NotifyCanExecuteChanged`。
- `AsyncRelayCommand.md`：异步命令、取消、`IsRunning`、`ExecutionTask`。
- `Messenger.md`：弱引用/强引用 Messenger、消息注册、请求消息。
- `Ioc.md`：`Ioc`、服务注册、服务解析、构造函数注入。
- `ObservableGroupedCollections.md`：可观察分组集合和分组 UI。
- `PuttingThingsTogether.md`：端到端示例，组合 service、viewmodel、IoC、command 和 XAML。

## SwiftExport 推荐写法对照

### 绑定属性

官方 `ObservableObject` 示例会展示手写 `SetProperty`：

```csharp
private string name;

public string Name
{
    get => name;
    set => SetProperty(ref name, value);
}
```

SwiftExport 新业务代码优先写：

```csharp
[ObservableProperty]
public partial string Name { get; set; } = string.Empty;
```

需要通知派生属性：

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(DisplayName))]
public partial string ProductName { get; set; } = string.Empty;

public string DisplayName => ProductName;
```

### 同步命令

官方 sample 可能展示构造函数中 `new RelayCommand(...)`。SwiftExport 新业务代码优先写：

```csharp
[RelayCommand(CanExecute = nameof(CanSave))]
private void Save()
{
}

private bool CanSave()
{
    return CanWrite && IsNotBusy;
}
```

依赖属性变化刷新命令：

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
public partial string Name { get; set; } = string.Empty;
```

### 异步命令

官方 `AsyncRelayCommand` 资料用于理解 `IsRunning`、取消和 `ExecutionTask`。SwiftExport 新业务代码优先写：

```csharp
[RelayCommand(CanExecute = nameof(CanSearch))]
private async Task SearchAsync()
{
    await RunBusyAsync(async () =>
    {
        // 调用 Refit API 或前端 service
    }, "正在查询...");
}

private bool CanSearch()
{
    return IsNotBusy;
}
```

需要取消时可以使用：

```csharp
[RelayCommand]
private async Task LoadAsync(CancellationToken cancellationToken)
{
}
```

### 验证

官方 `ObservableValidator` 示例适用于表单级验证。SwiftExport 使用时优先保持生成器写法：

```csharp
public sealed partial class ProductEditModel : ObservableValidator
{
    [ObservableProperty]
    [Required(ErrorMessage = "产品编码不能为空")]
    public partial string ProductCode { get; set; } = string.Empty;

    partial void OnProductCodeChanged(string value)
    {
        ValidateProperty(value, nameof(ProductCode));
    }
}
```

简单 DataGrid 行校验优先沿用项目 `BaseLineModel<TDto>` 的验证机制；只有表单模型确实需要 `INotifyDataErrorInfo` 时再用 `ObservableValidator`。

### Messenger

需要页面间弱耦合消息时参考 `Messenger.md`，但 SwiftExport 默认优先使用已有导航、Dialog、UI service 和明确的 ViewModel 协作。只有以下场景再考虑 Messenger：

- Shell 与子页面广播状态。
- 页面之间不适合直接引用。
- 需要解耦的一次性通知。

### IoC

官方 `Ioc.md` 可帮助理解依赖注入。SwiftExport 当前前端使用 `Microsoft.Extensions.DependencyInjection`，业务 ViewModel 通过构造函数注入 API、UI service、cache、dialog service，不直接 `new` 依赖。

## 不要照搬的内容

- 不要把 sample app 的 Reddit 示例业务结构当作 SwiftExport 目录结构。
- 不要把 WinUI `x:Bind` 复制到 WPF。
- 不要在新业务 ViewModel 中使用构造函数 `new RelayCommand` 作为默认写法。
- 不要为普通属性回退到手写 backing field + `SetProperty`。
- 不要在当前 .NET 10 客户端新增字段式 `[ObservableProperty] private ...`；统一使用 partial property。
- 不要把 sample 中通用命名替换 SwiftExport 的业务命名规范。
