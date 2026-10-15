---
name: wpf-dotnet10-communitytoolkit-mvvm
description: SwiftExport 当前 .NET 10 WPF 客户端的 CommunityToolkit.Mvvm 强制写法。用于新增或重构 SwiftExport.Client.Wpf 的 ViewModel、LineModel、TreeItemModel、EditModel、SearchModel、绑定属性、RelayCommand/AsyncRelayCommand、ObservableValidator 和 DI。要求优先使用源生成器，不允许把旧 MVVMLight 或手写 RelayCommand/SetProperty 样板带入新客户端。
---

# SwiftExport CommunityToolkit MVVM Skill

## 适用范围

用于 `SwiftExport.Client.Wpf` 当前 .NET 10 WPF 前端。

触发场景：

- 新增或修改 ViewModel。
- 新增或修改 LineModel、TreeItemModel、EditModel、SearchModel。
- 新增绑定属性、筛选条件、分页属性、选中项、状态消息。
- 新增同步或异步命令。
- 重构手写 `SetProperty`、`ICommand`、`RelayCommand`、`AsyncRelayCommand`。
- 修复 XAML Binding、Command、CanExecute、DataGrid 行状态。
- 实现或修改异步加载、搜索防抖、写命令并发、Busy、导航/关闭或资源释放时，同时读取 `../runtime-safety/SKILL.md`。
- 实现或修改页面服务边界、权限、普通表单验证、导航参数、Document Tab 或 Dialog 返回协议时，同时读取 `../page-architecture/SKILL.md`。
- 新增、拆分或合并 ViewModel、Model、Service、Adapter 或其他客户端文件时，同时读取 `../file-organization/SKILL.md`。

## 必读上下文

公共启动沿用 `AGENTS.md`；以下仅补齐当前业务任务尚未读取的依赖，不回溯已读入口。

执行前先读：

- 改变分层、公共依赖或边界不明时读 `.agents/references/工程架构总纲.md` 相关章节；局部属性/命令实现复用既有架构结论
- 目标页面和目标模型的真实源码

本 skill 已根据本地 `mvvm-toolkit-samples` 中的 `ObservableObject`、`RelayCommand`、`AsyncRelayCommand`、`ObservableValidator`、`Ioc` 参考资料项目化；不要再照搬 sample app 的通用命名和结构。

如果不确定 CommunityToolkit.Mvvm 的官方示例写法，按需读取：

- `references/官方MVVMToolkit示例.md`

## 核心规则

- 业务 ViewModel / Model 必须优先使用 CommunityToolkit 源生成器。
- ViewModel / Model 必须声明为 `partial class`。
- 绑定属性统一写成 `[ObservableProperty] public partial ... { get; set; }` partial property；不再新增字段式生成属性。
- 命令优先写成 `[RelayCommand] private ...` 方法。
- 命令刷新优先用 `[NotifyCanExecuteChangedFor(nameof(XxxCommand))]`。
- 依赖属性变化的派生属性优先用 `[NotifyPropertyChangedFor(nameof(Xxx))]`。
- 异步业务命令返回 `Task`，不要写 `async void`。
- 不要在业务 ViewModel 构造函数里 `new RelayCommand` / `new AsyncRelayCommand`。
- 不要在业务 ViewModel / LineModel 中为普通绑定属性手写 backing field + `SetProperty`。
- 不要把旧 MVVMLight 写法带进 `SwiftExport.Client.Wpf`。

## 标准 ViewModel 写法

推荐：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

public sealed partial class ProductsViewModel : ViewModelBaseEx
{
    private readonly IProductApi _productApi;

    public ProductsViewModel(IProductApi productApi)
    {
        _productApi = productApi;
        Title = "产品资料";

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(SaveCommand);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string? Keyword { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial ProductLineModel? SelectedLine { get; set; }

    [ObservableProperty]
    public partial string? ResultSummary { get; set; }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task SearchAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _productApi.SearchAsync(Keyword);
            ResultSummary = $"共 {result.TotalCount} 条";
        }, "正在查询产品...");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            // 保存逻辑由 ViewModel 编排，业务事务由后端 Application 执行。
        }, "正在保存产品...");
    }

    private bool CanRunCommand()
    {
        return IsNotBusy;
    }

    private bool CanSave()
    {
        return CanWrite && IsNotBusy && IsDirty;
    }
}
```

禁止：

```csharp
private string? _keyword;

public string? Keyword
{
    get => _keyword;
    set => SetProperty(ref _keyword, value);
}

public IAsyncRelayCommand SearchCommand { get; }

public ProductsViewModel()
{
    SearchCommand = new AsyncRelayCommand(SearchAsync);
}
```

## 命令规则

推荐：

```csharp
[RelayCommand(CanExecute = nameof(CanDelete))]
private async Task DeleteAsync(ProductLineModel? line)
{
    if (line is null)
    {
        return;
    }

    await RunWriteAsync(async () =>
    {
        await _productApi.DeleteAsync(line.Uid);
    }, "正在删除...");
}

private bool CanDelete(ProductLineModel? line)
{
    return CanWrite && IsNotBusy && line is not null;
}
```

规则：

- `[RelayCommand]` 标注 `Task` 方法时生成异步命令。
- 带参数方法会生成泛型命令。
- 生成命令名会自动追加 `Command`，异步方法名末尾的 `Async` 会被移除。
- 需要取消时，可以使用带 `CancellationToken` 参数的异步命令方法。
- 命令运行状态优先依赖生成命令的 `IsRunning` 或页面基类 `IsBusy`，不要自己增加重复状态字段。
- `[RelayCommand]` 的并发选项不能替代运行时门禁：查询可允许新请求进入以取消旧请求，写命令默认禁止并发；具体策略遵循 `../runtime-safety/SKILL.md`。

## 属性规则

推荐：

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(DisplayName))]
[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
public partial string? ProductName { get; set; }

public string DisplayName => string.IsNullOrWhiteSpace(ProductName)
    ? "(未命名产品)"
    : ProductName;

partial void OnProductNameChanged(string? value)
{
    ValidationSummary = null;
}
```

禁止：

- 在属性 setter 中调用 API。
- 在属性 setter 中执行保存、删除、过账等业务命令。
- 为普通属性手写 `SetProperty`。
- 用手写属性替代 `[NotifyPropertyChangedFor]` 和 `[NotifyCanExecuteChangedFor]`。

## LineModel 规则

`BaseLineModel<TDto>` 是 SwiftExport 行状态基类。基类监听 CommunityToolkit 的 `PropertyChanging/PropertyChanged`，业务 LineModel 使用源生成器后会自动推进脏状态；不要再为每个字段编写仅用于 `MarkAsModified()` 的 partial method。

```csharp
public sealed partial class ProductLineModel : BaseLineModel<ProductDto>
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial string ProductCode { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial string ProductName { get; set; } = string.Empty;

    public string DisplayText => $"{ProductCode} {ProductName}".Trim();

    protected override void LoadFromDto(ProductDto dto)
    {
        dto.Adapt(this);
    }

    public override ProductDto ToDto() => this.Adapt<ProductDto>();

    protected override bool IsTrackingIgnoredProperty(string? propertyName)
    {
        return base.IsTrackingIgnoredProperty(propertyName)
            || propertyName == nameof(DisplayText);
    }
}
```

规则：

- `Load(dto)` 已由基类使用 `SuppressScope()`；`LoadFromDto` 内可以直接赋值或 Mapster。
- DTO 与 LineModel 的字段差异、空值归一化和命名差异必须集中写入 `Mappings/MapsterConfig.cs`；具体模型中的 `LoadFromDto`/`ToDto` 只保留基类要求的一行适配入口，不手写逐字段映射。
- `[ObservableProperty]`、普通 `SetProperty` 和兼容期的 `SetTrackedProperty` 都由基类自动跟踪。
- `partial void OnXxxChanged` 只用于派生属性通知、局部 UI 状态或明确副作用，不再重复调用 `MarkAsModified()`。
- 计算属性、审计回填或纯 UI 属性若会发出完整的 PropertyChanging/Changed，使用 `IsTrackingIgnoredProperty` 排除。
- 计算属性用 `[NotifyPropertyChangedFor]`，不要手写 setter。
- 不重复声明 `IsChecked`、`IsSelected`、`IsReadOnly` 等基类已有 UI 状态。
- 不重复声明 `Uid`、`Version`、审计字段和持久化删除元数据；这些系统字段由 `BaseLineModel<TDto>` 统一承载，并默认排除脏跟踪。
- 服务端回填、Lookup 显示值同步等程序性赋值使用 `SuppressChangeTracking()`；最外层作用域结束后只接纳作用域内改变的属性，不能覆盖行上其他未保存修改。
- `Load(dto)` 与 `RejectChanges()` 使用基类内部低层抑制。页面导航、Tab 切换不得用抑制作用域包住，因为失焦提交的真实编辑仍应成为修改。
- `SetTrackedProperty` 仅为现有手写属性保留；新增 LineModel 不以它代替 CommunityToolkit 源生成器。

## 列表模型规则

- `BaseListModel.Items` 是只读可观察集合，页面只能绑定和读取，不能绕过基类直接 Add/Remove/Clear。
- 新增、删除、回滚必须使用 `AddNew`、`Remove`、`RemoveCompletely`、`RejectChanges`。
- 已持久化行删除后仍保留在内部删除池；回滚时按原始列表顺序恢复。
- Modified 行被删除后执行单步恢复时回到 Modified，不得错误变成 Unchanged。
- `GetChangeSet()` 只输出 UI 层通用变更集，不直接构造后端 Batch Request。
- `LoadFromDtos()` 必须先物化输入，再清空旧集合并解绑事件，允许安全传入当前集合的投影。
- 批量勾选、加载、回滚和清空使用 `SuppressListEvents()` 合并通知；抑制必须支持嵌套，最外层退出时统一发布一次 `ListStateChanged`。
- 批量业务读取使用 `AllLines`、`CheckedItems`、`AddedItems`、`ModifiedItems`、`DeletedItems`，不得通过反射或绕过基类访问删除池。

## 验证规则

- 同步跨字段校验使用 `BaseLineModel<TDto>.ValidateRow()`，异步校验使用 `ValidateAsync()`；列表保存前按同步、异步顺序执行。
- 普通表单优先由 EditModel/LineModel 继承 `ObservableValidator` 或实现 `INotifyDataErrorInfo`；ViewModel 负责保存前触发整体验证、聚合摘要和首错反馈。
- 使用 `ObservableValidator` 时，属性仍优先用生成器和 DataAnnotations，不回退到大量手写 setter。
- 跨字段和异步验证放到明确方法中；setter/`OnXxxChanged` 只允许清理受影响的旧错误、通知派生状态或调度可取消验证，不执行保存、导航或其他业务流程。
- 服务端结构化字段错误由页面模型按稳定字段键接纳；业务/状态失败保留在命令结果或页面摘要，不伪装成字段错误。失败不得清空输入和脏状态。

## DI / IoC 规则

- 使用 `Microsoft.Extensions.DependencyInjection`，ViewModel 依赖通过构造函数注入。
- View 不直接 new ViewModel；通过项目现有注册和 DataTemplate/Navigation 解析。
- CommunityToolkit 的 `Ioc` 示例不是本项目第二套容器，不得绕过现有组合根引入 Service Locator。
- ViewModel、Feature Service、UI Adapter 和 WPF-UI 类型各自承担什么职责、放在哪个文件，只服从 `../file-organization/SKILL.md` 与 `../page-architecture/SKILL.md`；本节不重复定义。

## 基类例外

以下基础设施类只有在属性具有派生 setter、事务状态或并发计数等无法等价生成的语义时，才可以保留少量手写实现：

- `ObservableObjectEx`
- `ViewModelBaseEx`
- `BaseLineModel<TDto>`
- `BaseListModel<TLine,TDto>`
- Shell 级基础设施模型

这些基类中的普通可绑定状态同样使用 partial property。新增业务页面、业务 ViewModel、业务 LineModel、业务树节点不得以基类例外为借口继续手写样板属性和命令。

## 修改现有代码策略

1. 不在无关任务中大面积重写。
2. 修改某个页面时，优先把本次触碰到的属性和命令迁移为源生成器写法。
3. 同一个 ViewModel 中不要长期混用手写命令和生成命令；若开始迁移，至少迁移当前相关命令组。
4. 迁移后检查 XAML 绑定名是否保持一致。
5. 迁移后运行：

```powershell
dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj
```

## 禁止事项

- 禁止新增 `public IRelayCommand XxxCommand { get; }` 并在构造函数中赋值。
- 禁止新增 `private T _field; public T Field { get => _field; set => SetProperty(...) }` 样板属性。
- 禁止新增 `[ObservableProperty] private T field;` 字段式生成属性；当前 .NET 10 客户端统一使用 public partial property。
- 禁止用 `ICommand` 作为业务命令默认类型。
- 禁止把 API 调用写在属性 setter 或 `partial void OnXxxChanged` 中。
- 禁止让 ViewModel 直接访问 Repository、SQL、Controller 或 Window。
- 禁止使用 MVVMLight、`GalaSoft.MvvmLight` 或旧 `RelayCommand`。
