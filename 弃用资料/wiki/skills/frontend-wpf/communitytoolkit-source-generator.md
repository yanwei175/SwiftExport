# Skill: CommunityToolkit MVVM 源生成器写法

本页是 `.codex/skills/swiftexport-communitytoolkit-mvvm/SKILL.md` 的 wiki 路由版。Codex 可自动发现的强制规则以 `.codex/skills/**/SKILL.md` 为准；本页用于 AI Wiki 索引和人工浏览。

## 适用场景

用于 `SwiftExport.Client.Wpf` 当前 .NET 10 WPF 前端的 ViewModel、LineModel、TreeItemModel、EditModel、SearchModel 和轻量可绑定模型。

典型触发语句：

- “写 ViewModel”
- “写 CommunityToolkit MVVM”
- “新增页面”
- “新增 LineModel”
- “新增 DataGrid 行模型”
- “新增命令”
- “重构手写 SetProperty”
- “不要手写 RelayCommand”

本 skill 依赖：

- [WPF CommunityToolkit MVVM Guide](../../guides/frontend-wpf/wpf-communitytoolkit-mvvm.md)
- [.NET 10 前端 MVVM 基础设计](../../project/dotnet10-frontend-mvvm-blueprint.md)

## 核心铁律

- 业务 ViewModel / Model 必须优先使用 CommunityToolkit 源生成器。
- ViewModel / Model 必须声明为 `partial class`。
- 绑定属性优先写成 `[ObservableProperty] private ...` 字段。
- 命令优先写成 `[RelayCommand] private ...` 方法。
- 命令刷新优先用 `[NotifyCanExecuteChangedFor(nameof(XxxCommand))]`。
- 依赖属性变更的派生属性优先用 `[NotifyPropertyChangedFor(nameof(Xxx))]`。
- 不要在业务 ViewModel 构造函数里 `new RelayCommand` / `new AsyncRelayCommand`。
- 不要在业务 ViewModel / LineModel 中为普通绑定属性手写 backing field + `SetProperty`。

## 官方生成器规则

CommunityToolkit 的生成器要求：

- `[ObservableProperty]` 标注字段所在类型必须是 `partial`。
- `[RelayCommand]` 标注方法所在类型必须是 `partial`。
- 字段名建议使用 `_lowerCamel` 或 `lowerCamel`，生成属性名会转为 `UpperCamel`。
- `[RelayCommand]` 标注 `Task` 方法时，会生成 `IAsyncRelayCommand`。
- `[RelayCommand]` 标注带参数方法时，会生成泛型命令。
- 生成命令名会自动追加 `Command`，异步方法名末尾的 `Async` 会被移除。

## ViewModel 标准写法

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
    private string? keyword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private ProductLineModel? selectedLine;

    [ObservableProperty]
    private string? resultSummary;

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
            // 保存逻辑由页面 ViewModel 编排，后端事务由 WebApi/Application 执行。
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

## RelayCommand 规则

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

- 异步业务命令返回 `Task`。
- 不写 `async void` 业务命令。
- 不暴露手写 `ICommand` 属性。
- 不在构造函数中手动 new 命令。
- 需要参数时直接在方法参数上表达。
- 命令 CanExecute 改变时，优先用 `[NotifyCanExecuteChangedFor]` 或在状态集中变化后调用生成的 `XxxCommand.NotifyCanExecuteChanged()`。

## ObservableProperty 规则

推荐：

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(DisplayName))]
[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
private string? productName;

public string DisplayName => string.IsNullOrWhiteSpace(ProductName)
    ? "(未命名产品)"
    : ProductName;
```

需要变更回调时，用生成器 partial method：

```csharp
[ObservableProperty]
private ProductLineModel? selectedLine;

partial void OnSelectedLineChanged(ProductLineModel? value)
{
    ValidationSummary = null;
}
```

禁止：

- 为普通绑定属性手写 backing field。
- 在 setter 中堆业务流程。
- 把 API 调用写进属性 setter。
- 用 setter 触发保存、过账、删除等业务命令。

## LineModel 写法

`BaseLineModel<TDto>` 当前仍提供 `SetTrackedProperty`，但新业务 LineModel 不应继续手写属性。新代码优先使用 `[ObservableProperty]`，并在 `partial void OnXxxChanged(...)` 中推进行状态。

推荐：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

public sealed partial class ProductLineModel : BaseLineModel<ProductDto>
{
    [ObservableProperty]
    private Guid uid;

    [ObservableProperty]
    private int version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string productCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string productName = string.Empty;

    public string DisplayText => $"{ProductCode} {ProductName}".Trim();

    partial void OnProductCodeChanged(string value) => MarkModifiedIfTracking();

    partial void OnProductNameChanged(string value) => MarkModifiedIfTracking();

    protected override void LoadFromDto(ProductDto dto)
    {
        dto.Adapt(this);
    }

    public override ProductDto ToDto()
    {
        return this.Adapt<ProductDto>();
    }

    private void MarkModifiedIfTracking()
    {
        if (!IsSuppressed)
        {
            MarkAsModified();
        }
    }
}
```

规则：

- `Load(dto)` 已在基类中使用 `SuppressScope()`；`LoadFromDto` 内可以直接赋值或 Mapster。
- `partial void OnXxxChanged` 必须检查 `IsSuppressed` 后再 `MarkAsModified()`。
- 计算属性用 `[NotifyPropertyChangedFor]`，不要手写 setter。
- UI 状态如 `IsChecked`、`IsSelected`、`IsReadOnly` 优先使用基类已有属性，不在子类重复声明。
- 复杂字段映射放 Mapping 配置，不在 ViewModel 中复制几十个字段。

## TreeItem / StateModel 写法

轻量树节点或状态模型可以直接继承 `ObservableObject` 或项目轻量基类，但仍必须使用源生成器：

```csharp
public sealed partial class ProductCategoryTreeItemModel : ObservableObject
{
    [ObservableProperty]
    private string categoryName = string.Empty;

    [ObservableProperty]
    private bool isExpanded;

    partial void OnCategoryNameChanged(string value)
    {
        MarkModified();
    }
}
```

不要为了拿到 Busy、Dialog、Navigation，让树节点继承 `ViewModelBaseEx`。

## 基类例外

以下基础设施类可以保留少量手写 `SetProperty`，因为它们承载通用状态机或需要 protected/private setter：

- `ObservableObjectEx`
- `ViewModelBaseEx`
- `BaseLineModel<TDto>`
- `BaseListModel<TLine,TDto>`
- Shell 级基础设施模型

但新增业务页面、业务 ViewModel、业务 LineModel、业务树节点不得以这些基类为借口继续手写样板属性和命令。

## 修改现有代码策略

遇到当前手写 MVVM 代码时：

1. 不在无关任务中大面积重写。
2. 修改某个页面时，优先把该页面本次触碰到的属性和命令迁移为源生成器写法。
3. 同一个 ViewModel 中不要长期混用“手写命令”和“生成命令”；若开始迁移，至少迁移当前相关命令组。
4. 迁移后必须检查 XAML 绑定名是否保持一致。
5. 迁移后必须运行 `dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj`。

## 禁止事项

- 禁止新增 `public IRelayCommand XxxCommand { get; }` 并在构造函数中赋值。
- 禁止新增 `private T _field; public T Field { get => _field; set => SetProperty(...) }` 这类普通样板属性。
- 禁止用 `ICommand` 作为业务命令默认类型。
- 禁止把 API 调用写在属性 setter 或 `partial void OnXxxChanged` 中。
- 禁止让 ViewModel 直接访问 Repository、SQL、Controller 或 Window。
- 禁止把旧 MVVMLight 写法带进 `SwiftExport.Client.Wpf`。
