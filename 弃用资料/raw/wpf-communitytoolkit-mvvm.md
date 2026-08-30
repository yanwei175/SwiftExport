# Skill: WPF CommunityToolkit MVVM

## 适用范围

本 skill 适用于：

- .NET 8+ WPF
- `CommunityToolkit.Mvvm`
- `Microsoft.Extensions.DependencyInjection`
- ERP / DataGrid / 本地客户端项目
- 可编辑表格、批量保存、批量删除、批量勾选、脏数据跟踪、撤销编辑

不适用于继续使用 MVVMLight 的旧项目。新项目不要混用 `GalaSoft.MvvmLight`。

## 基本原则

WPF 只负责界面层。推荐调用方向：

```text
View
    -> Binding / Command
ViewModel
    -> Application Service
    -> Repository Interface
    -> Repository Implementation / Dapper / SQLite
```

ViewModel 可以包含：

- 页面状态
- Binding 属性
- Command
- 调用 Application Service
- 调用 DialogService / MessageService / NavigationService
- 简单 UI 编排

ViewModel 禁止包含：

- SQL / Dapper / SQLite 访问细节
- Repository 实现
- 直接 `new Repository`
- 直接 `new AppService`
- 业务事务控制
- 复杂业务计算
- 大量字段映射复制

## 最终基类

最终版只维护拆分文件，不再维护汇总代码文件。

```text
ObservableObjectEx
    只提供 SuppressScope / IsSuppressed 抑制机制。

ViewModelBaseEx
    页面级状态、Busy、只读写入保护、异常包装。

BaseLineModel<TDto>
    DataGrid 单行状态、DTO 转换、验证、脏数据、回滚、提交。

BaseListModel<TLine, TDto>
    DataGrid 集合、SelectedItem、删除池、批量勾选、变更集、列表级验证。

ModelState / IGridLineModel / GridChangeSet<TDto>
    行状态、最小接口、UI 层通用变更集。
```

不要让 `BaseLineModel<TDto>` 继承 `ViewModelBaseEx`。

原因：

- 行模型不应该拥有 Busy、导航、弹窗、消息提示等页面级能力。
- 页面状态和行状态必须分离。
- 否则每一行都会带上页面级职责，维护成本会被放大。

## 推荐继承方式

页面 ViewModel：

```c#
public partial class ProductPriceViewModel : ViewModelBaseEx
{
}
```

行模型：

```c#
public partial class ProductPriceLineModel : BaseLineModel<ProductPriceDto>
{
}
```

集合模型：

```c#
public sealed class ProductPriceListModel
    : BaseListModel<ProductPriceLineModel, ProductPriceDto>
{
}
```

## 必要 NuGet

```xml
<PackageReference Include="CommunityToolkit.Mvvm" Version="*" />
<PackageReference Include="Mapster" Version="*" />
```

如果不用 Mapster，可以 override `LoadFromDto` 和 `ToDto` 手写映射。

## ObservableObjectEx 规则

`ObservableObjectEx` 只提供抑制机制：

- `SuppressScope()`
- `IsSuppressed`

它不包含：

- Busy
- Dialog
- Navigation
- Message
- Repository
- SQL
- 业务保存流程

`BaseLineModel<TDto>` 和 `ViewModelBaseEx` 都继承它，用同一套方式表达“程序内部赋值不触发状态机”。

## ViewModelBaseEx 规则

`ViewModelBaseEx` 用于页面级通用状态：

- `Title`
- `BusyMessage`
- `ValidationSummary`
- `IsDirty`
- `IsBusy`
- `IsNotBusy`
- `IsReadOnly`
- `CanWrite`
- `RunBusyAsync`
- `RunWriteAsync`
- `RegisterBusyNotifyCommand`

`RunBusyAsync` 规则：

- 自动维护 Busy 计数，支持嵌套调用。
- `OperationCanceledException` 静默处理。
- 其它异常进入 `OnExceptionAsync`。

`RunWriteAsync` 规则：

- `IsReadOnly == true` 时不执行写操作。
- 只读违规进入 `OnReadOnlyViolationAsync`。

## BaseLineModel<TDto> 规则

`BaseLineModel<TDto>` 用于 DataGrid 单行状态。

应包含：

- `Uid`
- `Version`
- `State`
- `IsDirty / IsNew / IsModified / IsDeleted`
- `IsChecked / IsSelected / IsReadOnly`
- `RowIndex`
- `LoadFromDto`
- `ToDto`
- `MarkAsAdded / MarkAsModified / MarkAsDeleted / RestoreFromDeleted`
- `AcceptChanges / RejectChanges`
- `ValidateRow / ValidateAsync / ForceValidationUI`

`Uid` 和 `Version` 是所有可编辑行都需要的基础协议字段：

- `Uid` 用于区分新增行和已持久化行，也用于删除集提交。
- `Version` 用于乐观并发控制。

审计字段不放在 `BaseLineModel<TDto>` 中。  
如果某个业务行需要 `CreatedDate / UpdatedDate / CreatedBy / UpdatedBy`，在具体 LineModel 子类中用 `[ObservableProperty]` 自行声明，并按需 override `IsTrackingIgnoredProperty` 排除这些审计字段。

### 自动变更追踪

最终版不再依赖重定义 `SetProperty` 的方案。

`BaseLineModel<TDto>` 在构造函数中订阅自身：

- `PropertyChanging`：首次修改前记录原始值。
- `PropertyChanged`：字段变更后自动推进状态机。

因此子类优先使用 Source Generator：

```csharp
public partial class ProductPriceLineModel : BaseLineModel<ProductPriceDto>
{
    [ObservableProperty] private string? companyModel;
	[ObservableProperty] private decimal quantity = 1;
    [ObservableProperty] private decimal accountingPrice;
    
    public decimal LineAccountingPrice =>
        Math.Round(AccountingPrice * Quantity, 2, MidpointRounding.AwayFromZero);

    partial void OnQuantityChanged(decimal value)
    {
        OnPropertyChanged(nameof(LineAccountingPrice));
    }

    partial void OnAccountingPriceChanged(decimal value)
    {
        OnPropertyChanged(nameof(LineAccountingPrice));
    }

    public override string this[string columnName]
    {
        get
        {
            if (columnName == nameof(CompanyModel) && string.IsNullOrWhiteSpace(CompanyModel))
            {
                return "型号不能为空。";
            }

            if (columnName == nameof(AccountingPrice) && AccountingPrice < 0)
            {
                return "核算价不能小于 0。";
            }

            return string.Empty;
        }
    }
}
```

程序内部赋值不希望触发脏状态时，使用：

```csharp
using (SuppressChangeTracking())
{
    TotalAmount = Qty * UnitPrice;
}
```

### 删除恢复语义

`RestoreFromDeleted` 必须保留真实状态：

- 新增行删除后恢复：回到 `Added`。
- 已存在且未修改行删除后恢复：回到 `Unchanged`。
- 已修改行删除后恢复：回到 `Modified`。

### 验证规则

支持三层验证：

1. 字段级：override `IDataErrorInfo.this[string columnName]`。
2. 行级：override `ValidateRow()`。
3. 异步：override `ValidateAsync()`。

如果需要 DataAnnotations，override：

```csharp
protected override bool EnableDataAnnotationsValidation => true;
```

## BaseListModel<TLine, TDto> 规则

`BaseListModel<TLine, TDto>` 用于 DataGrid 行集合。

应包含：

- `Items`
- `AllLines`
- `SelectedItem`
- `HasChanges`
- `IsAllValid`
- `IsAllChecked`
- `CheckedCount`
- `CheckedItems`
- `AddNew / Add / AddFirst`
- `Remove / RemoveCompletely`
- `Clear`
- `LoadFromDtos`
- `GetAddedLines / GetModifiedLines / GetDeletedLines / GetChangedLines`
- `GetDeletedIds`
- `GetChangeSet`
- `AcceptChanges / RejectChanges`
- `ValidateAllAsync`
- `Reindex`

### 删除池规则

`Items` 是 UI 可见集合。

`AllItems` 是内部完整集合，包含已经逻辑删除的行。

删除已存在行：

```text
从 Items 移除
在 AllItems 保留
State = Deleted
保存时进入 DeletedIds
```

删除新增行：

```text
直接从 Items 和 AllItems 移除
不进入变更集
```

保存成功：

```csharp
Lines.AcceptChanges();
AcceptChanges();
```

取消编辑：

```csharp
Lines.RejectChanges();
RejectChanges();
```

### 变更集规则

`BaseListModel` 输出 UI 层通用结构：

```csharp
GridChangeSet<TDto>
```

不要让 `BaseListModel` 直接输出后端请求 DTO，例如 `BatchRequestDto<TDto>`。

后端请求结构由 ViewModel 或 Application Service 层适配：

```csharp
var changeSet = Lines.GetChangeSet();

await _productPriceAppService.SaveAsync(
    changeSet.Added,
    changeSet.Modified,
    changeSet.DeletedIds);
```

### 列表验证规则

`ValidateAllAsync` 必须先检查同步验证，再执行异步验证：

```text
line.IsValid / line.Error
    -> line.ValidateAsync()
```

这样才能覆盖字段级、行级、异步三类错误。

### 命令刷新规则

`BaseListModel` 不直接保存 Command。

推荐在页面 ViewModel 中订阅：

```csharp
Lines.ListStateChanged += (_, _) =>
{
    SaveCommand.NotifyCanExecuteChanged();
    DeleteCommand.NotifyCanExecuteChanged();
};
```

原因：

- 列表基类不应该依赖具体命令类型。
- 页面决定哪些命令受列表状态影响。
- 更适合 CommunityToolkit.Mvvm 的 `[RelayCommand]` 模式。

## DataGrid 绑定规则

可编辑 DataGrid 不直接绑定 DTO。

推荐流程：

```text
Application DTO
    -> LineModel
    -> BaseListModel
    -> DataGrid
    -> 保存时 GetChangeSet()
    -> Application Service
```

ViewModel 示例：

```csharp
public ProductPriceListModel Lines { get; } = new();
```

XAML 示例：

```xml
<DataGrid ItemsSource="{Binding Lines.Items}"
          SelectedItem="{Binding Lines.SelectedItem}"
          AutoGenerateColumns="False"
          CanUserAddRows="False" />
```

## 页面加载规则

不要在 ViewModel 构造函数中执行大量异步加载。

推荐：

```csharp
[RelayCommand]
private async Task LoadAsync()
{
    await RunBusyAsync(async () =>
    {
        var result = await _productPriceAppService.GetListAsync();
        Lines.LoadFromDtos(result);
        Lines.AcceptChanges();
        AcceptChanges();
    }, "正在加载数据...");
}
```

构造函数只做：

- 保存注入对象
- 初始化简单默认值
- 初始化集合
- 注册列表状态变化事件

## 保存规则

保存流程：

```text
BaseListModel.GetChangeSet()
    -> Application Service
    -> Repository
```

示例：

```csharp
[RelayCommand(CanExecute = nameof(CanSave))]
private async Task SaveAsync()
{
    await RunWriteAsync(async () =>
    {
        var error = await Lines.ValidateAllAsync();
        if (!string.IsNullOrWhiteSpace(error))
        {
            ValidationSummary = error;
            return;
        }

        var changeSet = Lines.GetChangeSet();

        await _productPriceAppService.SaveAsync(
            changeSet.Added,
            changeSet.Modified,
            changeSet.DeletedIds);

        Lines.AcceptChanges();
        AcceptChanges();

        _messageService.ShowInfo("保存成功。");
    }, "正在保存数据...");
}

private bool CanSave()
{
    return CanWrite && Lines.HasChanges && Lines.IsAllValid;
}
```

ViewModel 不开启数据库事务。事务由 Application Service 控制。

## 弹窗 / 消息 / 导航规则

ViewModel 不直接 `new Window`。

禁止：

```csharp
var window = new ProductEditWindow();
window.ShowDialog();
```

推荐：

以下服务不是 CommunityToolkit.Mvvm 内置，需要项目按需自行定义接口与实现，并通过 DI 注入 ViewModel。

```csharp
await _dialogService.ShowProductEditAsync(dto);
_messageService.ShowInfo("保存成功。");
_navigationService.NavigateTo("ProductPrice");
```

Service 接口放在 Client 层。Application 层不应该知道 WPF 弹窗。

## Mapster 规则

LineModel 可以使用 Mapster 做简单转换。

允许：

```csharp
public override void LoadFromDto(ProductPriceDto dto, bool acceptChanges = true)
{
    using (SuppressChangeTracking())
    {
        dto.Adapt(this);
    }

    if (acceptChanges)
    {
        AcceptChanges();
    }
}
```

复杂映射放到专门的 Mapping 配置中。

禁止在 ViewModel 中写大量字段复制。

## XAML 规则

XAML 负责：

- 布局
- Binding
- Style 引用
- DataGrid 列定义
- 简单触发器
- 简单 UI 行为

XAML 不写业务逻辑。

Binding 名称要稳定，避免随意改名导致页面失效。

## 文件大小规则

ViewModel 超过 800 行时，优先拆分。

拆分方向：

- 查询条件模型
- 行模型
- 集合模型
- DialogService
- ImportExportService
- Application Service

不要继续往一个 ViewModel 里堆所有逻辑。

## 禁止混用

本 skill 使用 CommunityToolkit.Mvvm。

禁止混入：

- GalaSoft.MvvmLight
- MVVMLight ViewModelBase
- MVVMLight RelayCommand
- MVVMLight Messenger

不要在 .NET 8+ 新项目中继续引入 MVVMLight。

## Codex 修改要求

修改 WPF CommunityToolkit MVVM 代码时必须：

1. 先确认项目引用 `CommunityToolkit.Mvvm`。

2. 不改变现有 UI 行为，除非用户明确要求。

3. 不把 SQL 写进 ViewModel。

4. 不在 ViewModel 中 `new Repository`。

5. 不混用 MVVMLight。

6. 保持 Binding 属性名称稳定。

7. 每次优先只处理一个页面或一个 ViewModel。

8. DataGrid 可编辑行优先使用 LineModel。

9. 页面通用状态优先放到 `ViewModelBaseEx`。

10. 行状态优先放到 `BaseLineModel<TDto>`。

11. 列表状态优先放到 `BaseListModel<TLine, TDto>`。

12. 不把业务保存流程塞进 `BaseLineModel` 或 `BaseListModel`。

13. 不让 `BaseListModel` 直接依赖后端请求 DTO。

14. 删除已有行时必须进入删除池，不要直接丢失 `DeletedIds`。

15. 删除新增行时可以 `RemoveCompletely`，不进入删除集。

16. 保存成功后必须 `Lines.AcceptChanges()`。

17. 取消编辑时优先 `Lines.RejectChanges()`。

## 使用Demo样本

```c#
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ProductPriceManager.App.Client.Mvvm.Examples;

/// <summary>
/// 示例 DTO：来自 Application Service，也会在保存时提交回 Application Service。
/// 注意：DTO 不是直接绑定到 DataGrid 的对象，DataGrid 应绑定 ProductPriceLineModel。
/// </summary>
public sealed class ProductPriceDto
{
    public Guid Uid { get; set; }
    public int Version { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public decimal Qty { get; set; }
    public decimal AccountingPrice { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// 示例行模型。
/// 这里演示：
/// 1. 用 [ObservableProperty] 定义业务字段。
/// 2. Uid / Version 来自 BaseLineModel。
/// 3. 审计字段不在基类中，需要的业务行自己声明。
/// 4. 计算属性 TotalAmount 不参与脏数据跟踪。
/// 5. 同时使用字段级验证、行级验证和异步验证。
/// </summary>
public sealed partial class ProductPriceLineModel : BaseLineModel<ProductPriceDto>
{
    [ObservableProperty]
    private string? productCode;

    [ObservableProperty]
    private string? productName;

    [ObservableProperty]
    private decimal qty = 1;

    [ObservableProperty]
    private decimal accountingPrice;

    [ObservableProperty]
    private DateTime? CreatedDate;

    [ObservableProperty]
    private DateTime? UpdatedDate;

    [ObservableProperty]
    private string? createdBy;

    [ObservableProperty]
    private string? UpdatedBy;

    /// <summary>
    /// 计算字段：只展示，不提交编辑状态。
    /// Qty 或 AccountingPrice 改变时手动通知 UI 刷新。
    /// </summary>
    public decimal TotalAmount => Qty * AccountingPrice;

    partial void OnQtyChanged(decimal value)
    {
        OnPropertyChanged(nameof(TotalAmount));
    }

    partial void OnAccountingPriceChanged(decimal value)
    {
        OnPropertyChanged(nameof(TotalAmount));
    }

    /// <summary>
    /// 审计字段和计算字段不应该因为 UI 回填而污染脏状态。
    /// </summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName)
    {
        return base.IsTrackingIgnoredProperty(propertyName)
               || propertyName == nameof(CreatedDate)
               || propertyName == nameof(UpdatedDate)
               || propertyName == nameof(CreatedBy)
               || propertyName == nameof(UpdatedBy)
               || propertyName == nameof(TotalAmount);
    }

    /// <summary>
    /// 字段级验证：WPF DataGrid 单元格可直接通过 IDataErrorInfo 触发。
    /// </summary>
    public override string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(ProductCode) when string.IsNullOrWhiteSpace(ProductCode) => "商品编码不能为空。",
                nameof(ProductName) when string.IsNullOrWhiteSpace(ProductName) => "商品名称不能为空。",
                nameof(Qty) when Qty <= 0 => "数量必须大于 0。",
                nameof(AccountingPrice) when AccountingPrice < 0 => "核算价不能小于 0。",
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// 行级验证：适合跨字段规则。
    /// </summary>
    public override string? ValidateRow()
    {
        if (Qty > 0 && AccountingPrice == 0)
        {
            return "数量大于 0 时，核算价不能为 0。";
        }

        return null;
    }

    /// <summary>
    /// 异步验证：真实项目中通常在 ViewModel 保存前调用服务完成。
    /// 这里仅演示入口，避免行模型直接依赖 Repository 或 SQL。
    /// </summary>
    public override Task<string?> ValidateAsync()
    {
        if (string.Equals(ProductCode, "DUPLICATE", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>("商品编码已存在。");
        }

        return Task.FromResult<string?>(null);
    }
}

/// <summary>
/// 示例集合模型。
/// 继承 BaseListModel 后自动拥有 Items、删除池、变更集、批量勾选、提交和回滚能力。
/// </summary>
public sealed class ProductPriceListModel : BaseListModel<ProductPriceLineModel, ProductPriceDto>
{
    public override ProductPriceLineModel CreateNewLine()
    {
        var line = base.CreateNewLine();
        line.ProductCode = string.Empty;
        line.ProductName = string.Empty;
        line.Qty = 1;
        line.AccountingPrice = 0;
        return line;
    }
}

/// <summary>
/// 应用服务接口示例。
/// ViewModel 调用 Application Service；事务、Repository、SQL 都应放在 Application/Infrastructure 层。
/// </summary>
public interface IProductPriceAppService
{
    Task<IReadOnlyList<ProductPriceDto>> GetListAsync();

    Task SaveAsync(
        IReadOnlyList<ProductPriceDto> added,
        IReadOnlyList<ProductPriceDto> modified,
        IReadOnlyList<Guid> deletedIds);
}

/// <summary>
/// 消息服务不是 CommunityToolkit.Mvvm 内置能力，需要项目自行定义并通过 DI 注入。
/// </summary>
public interface IMessageService
{
    void ShowInfo(string message);
    void ShowError(string message);
}

/// <summary>
/// 弹窗服务不是 CommunityToolkit.Mvvm 内置能力，用于避免 ViewModel 直接 new Window。
/// </summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string message);
}

/// <summary>
/// 导航服务不是 CommunityToolkit.Mvvm 内置能力，用于避免 ViewModel 直接依赖具体页面。
/// </summary>
public interface INavigationService
{
    void NavigateTo(string route);
}

/// <summary>
/// 示例页面 ViewModel。
/// 这里演示：
/// 1. RunBusyAsync / RunWriteAsync。
/// 2. CanWrite / IsReadOnly。
/// 3. Lines.ListStateChanged 刷新命令。
/// 4. LoadFromDtos / GetChangeSet / AcceptChanges / RejectChanges。
/// 5. 批量勾选与删除池。
/// </summary>
public sealed partial class ProductPriceViewModel : ViewModelBaseEx
{
    private readonly IProductPriceAppService _appService;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;

    public ProductPriceViewModel(
        IProductPriceAppService appService,
        IMessageService messageService,
        IDialogService dialogService,
        INavigationService navigationService)
    {
        _appService = appService;
        _messageService = messageService;
        _dialogService = dialogService;
        _navigationService = navigationService;

        Title = "商品价格维护";

        Lines.ListStateChanged += (_, _) =>
        {
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCheckedCommand.NotifyCanExecuteChanged();
            RejectCommand.NotifyCanExecuteChanged();
        };

        RegisterBusyNotifyCommand(LoadCommand);
        RegisterBusyNotifyCommand(AddCommand);
        RegisterBusyNotifyCommand(SaveCommand);
        RegisterBusyNotifyCommand(DeleteCheckedCommand);
        RegisterBusyNotifyCommand(RejectCommand);
    }

    public ProductPriceListModel Lines { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var dtos = await _appService.GetListAsync();
            Lines.LoadFromDtos(dtos);
            Lines.AcceptChanges();
            AcceptChanges();
        }, "正在加载商品价格...");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Add()
    {
        Lines.AddNew();
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteChecked))]
    private async Task DeleteCheckedAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync($"确定删除选中的 {Lines.CheckedCount} 行吗？");
        if (!confirmed)
        {
            return;
        }

        foreach (var line in Lines.CheckedItems.ToList())
        {
            Lines.Remove(line);
        }

        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var error = await Lines.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error;
                return;
            }

            var changeSet = Lines.GetChangeSet();
            if (!changeSet.HasChanges)
            {
                return;
            }

            await _appService.SaveAsync(
                changeSet.Added,
                changeSet.Modified,
                changeSet.DeletedIds);

            Lines.AcceptChanges();
            AcceptChanges();
            _messageService.ShowInfo("保存成功。");
        }, "正在保存商品价格...");
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        Lines.RejectChanges();
        RejectChanges();
        ValidationSummary = null;
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo("Home");
    }

    private bool CanEdit()
    {
        return CanWrite;
    }

    private bool CanDeleteChecked()
    {
        return CanWrite && Lines.CheckedCount > 0;
    }

    private bool CanSave()
    {
        return CanWrite && Lines.HasChanges && Lines.IsAllValid;
    }

    private bool CanReject()
    {
        return IsNotBusy && Lines.HasChanges;
    }

    protected override Task OnExceptionAsync(Exception ex)
    {
        ValidationSummary = ex.Message;
        _messageService.ShowError(ex.Message);
        return Task.CompletedTask;
    }

    protected override Task OnReadOnlyViolationAsync()
    {
        _messageService.ShowError("当前页面为只读模式，不能执行写操作。");
        return Task.CompletedTask;
    }
}

```

