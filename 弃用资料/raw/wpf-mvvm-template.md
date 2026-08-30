# WPF CommunityToolkit MVVM 模板

## 适用范围

本模板适用于当前项目的 WPF 页面 ViewModel：

- `.NET 8 WPF`
- `CommunityToolkit.Mvvm`
- `Microsoft.Extensions.DependencyInjection`
- 页面级 ViewModel 继承统一基类
- Repository 通过 Application Service 或接口注入，不在 ViewModel 中直接创建

## 分层约定

调用方向固定为：

```text
View
    -> Binding / Command
ViewModel
    -> Application Service / UI Service
Application Service
    -> Repository Interface
Repository
    -> SQLite / Dapper
```

ViewModel 可以包含：

- 页面状态
- Binding 属性
- Command
- 简单 UI 编排
- 调用 Application Service
- 调用 DialogService / MessageService / NavigationService

ViewModel 禁止包含：

- SQL / Dapper / SQLite 细节
- Repository 实现
- `new Repository`
- `new Application Service`
- 业务事务控制
- 复杂业务计算
- 大量 DTO 字段复制

## ViewModelBase 模板

页面级 ViewModel 统一继承 `ViewModelBase`。

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using ProductPriceManager.App.Application.System.InterfacesRepository;
using ProductPriceManager.App.Services;
using WpfMessageBox = ProductPriceManager.App.Client.Wpf.Services.Implementations.AppDialogService;

namespace ProductPriceManager.App.Client.Wpf.MVVM;

public abstract partial class ViewModelBase(
    AppStatusService appStatus,
    IDatabaseHealthRepository databaseHealthService) : ObservableObject
{
    protected AppStatusService AppStatus { get; } = appStatus;
    protected IDatabaseHealthRepository DatabaseHealthService { get; } = databaseHealthService;

    public bool IsReadOnly => AppStatus.IsReadOnly;
    public bool IsBusy => AppStatus.IsBusy;

    protected void SetStatus(string message)
    {
        AppStatus.StatusMessage = message;
    }

    protected async Task RunAsync(Func<Task> action)
    {
        if (AppStatus.IsBusy)
        {
            return;
        }

        try
        {
            AppStatus.IsBusy = true;
            RefreshCommandState();

            await action();
        }
        catch (Exception ex)
        {
            AppStatus.StatusMessage = ex.Message;
            WpfMessageBox.Show(
                ex.Message,
                "产品采购价格管理",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            AppStatus.IsBusy = false;
            RefreshCommandState();
        }
    }

    protected async Task RunWriteAsync(Func<Task> action)
    {
        if (AppStatus.IsReadOnly)
        {
            WpfMessageBox.Show(
                AppStatus.StatusMessage,
                "只读模式",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        await RunAsync(action);

        await DatabaseHealthService.RefreshAsync();
        AppStatus.IsReadOnly = DatabaseHealthService.IsReadOnly;
        RefreshCommandState();
    }

    protected bool CanWrite()
    {
        return !AppStatus.IsReadOnly && !AppStatus.IsBusy;
    }

    protected virtual void RefreshCommandState()
    {
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(IsBusy));
    }
}
```

### 基类职责

`ViewModelBase` 只放页面通用状态和通用保护逻辑：

- `IsReadOnly`
- `IsBusy`
- `SetStatus`
- `RunAsync`
- `RunWriteAsync`
- `CanWrite`
- `RefreshCommandState`

不要把以下内容放进基类：

- 具体页面保存逻辑
- 具体页面查询逻辑
- 产品 / 供应商 / BOM 等业务规则
- SQL
- Repository 实现
- DataGrid 行模型字段

## 页面 ViewModel 模板

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductPriceManager.App.Application.System.InterfacesRepository;
using ProductPriceManager.App.Client.Wpf.MVVM;
using ProductPriceManager.App.Services;

namespace ProductPriceManager.App.Client.Wpf.ViewModels;

public partial class XxxViewModel(
    AppStatusService appStatus,
    IDatabaseHealthRepository databaseHealthService,
    IXxxAppService xxxAppService)
    : ViewModelBase(appStatus, databaseHealthService)
{
    private readonly IXxxAppService _xxxAppService = xxxAppService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private XxxLineModel? selectedItem;

    [ObservableProperty]
    private string? keyword;

    public XxxListModel Lines { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var items = await _xxxAppService.GetListAsync(Keyword);

            Lines.Items.Clear();

            foreach (var dto in items)
            {
                var line = new XxxLineModel();
                line.LoadFromDto(dto);
                Lines.Add(line);
            }

            Lines.AcceptChanges();
            SetStatus("加载完成。");
        });
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var changedDtos = Lines
                .GetChangedLines()
                .Select(x => x.ToDto())
                .ToList();

            await _xxxAppService.SaveAsync(changedDtos);

            Lines.AcceptChanges();
            SetStatus("保存成功。");
        });
    }

    private bool CanSave()
    {
        return CanWrite() && Lines.HasChanges;
    }

    protected override void RefreshCommandState()
    {
        base.RefreshCommandState();
        SaveCommand.NotifyCanExecuteChanged();
    }
}
```

> 如果页面命令需要跟随 `IsBusy` / `IsReadOnly` 变化刷新，在页面 ViewModel 中调用对应 `Command.NotifyCanExecuteChanged()`。

## 属性规则

优先使用 CommunityToolkit Source Generator：

```csharp
[ObservableProperty]
private string? productName;
```

需要属性变化后刷新命令时：

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
private ProductLineModel? selectedItem;
```

只有在 Source Generator 不适用时，才手写 `SetProperty`。

## 命令规则

异步命令使用 `Task`：

```csharp
[RelayCommand]
private async Task RefreshAsync()
{
    await LoadAsync();
}
```

带写入权限保护的命令使用 `RunWriteAsync`：

```csharp
[RelayCommand(CanExecute = nameof(CanDelete))]
private async Task DeleteAsync()
{
    await RunWriteAsync(async () =>
    {
        await _xxxAppService.DeleteAsync(SelectedItem!.Id);
    });
}

private bool CanDelete()
{
    return CanWrite() && SelectedItem is not null;
}
```

禁止在 ViewModel 中写：

```csharp
async void Save()
{
}
```

除非它是 WPF 事件处理器。

## DataGrid 模板

可编辑 DataGrid 不直接绑定 DTO，推荐结构：

```text
DTO
    -> LineModel
    -> ListModel
    -> DataGrid
    -> ToDto()
    -> Application Service
```

XAML：

```xml
<DataGrid ItemsSource="{Binding Lines.Items}"
          SelectedItem="{Binding Lines.SelectedItem}"
          AutoGenerateColumns="False">
</DataGrid>
```

行模型：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace ProductPriceManager.App.Client.Wpf.Models;

public partial class XxxLineModel : BaseLineModel<XxxDto>
{
    [ObservableProperty]
    private string? code;

    [ObservableProperty]
    private string? name;

    public override void LoadFromDto(XxxDto dto)
    {
        Code = dto.Code;
        Name = dto.Name;
        AcceptChanges();
    }

    public override XxxDto ToDto()
    {
        return new XxxDto
        {
            Code = Code,
            Name = Name
        };
    }

    partial void OnCodeChanged(string? value)
    {
        MarkAsModified();
    }

    partial void OnNameChanged(string? value)
    {
        MarkAsModified();
    }
}
```

## DI 注册模板

ViewModel 使用构造函数注入：

```csharp
services.AddSingleton<AppStatusService>();
services.AddTransient<XxxViewModel>();
services.AddScoped<IXxxAppService, XxxAppService>();
```

禁止：

```csharp
var repository = new XxxRepository();
var service = new XxxAppService(repository);
```

## 消息与弹窗

当前项目可继续使用：

```csharp
using WpfMessageBox = ProductPriceManager.App.Client.Wpf.Services.Implementations.AppDialogService;
```

后续如果抽象为 `IMessageService`，则推荐让 ViewModel 注入接口：

```csharp
private readonly IMessageService _messageService;

_messageService.ShowInfo("保存成功。");
_messageService.ShowError(ex.Message);
```

不要在 ViewModel 中直接 `new Window()`。

## 修改检查清单

改造或新增 WPF MVVM 代码时，按以下顺序检查：

1. 项目已引用 `CommunityToolkit.Mvvm`
2. ViewModel 继承 `ViewModelBase`
3. 属性优先使用 `[ObservableProperty]`
4. 命令优先使用 `[RelayCommand]`
5. 写操作使用 `RunWriteAsync`
6. 只读状态使用 `CanWrite`
7. 不在 ViewModel 中写 SQL
8. 不在 ViewModel 中 `new Repository`
9. DataGrid 可编辑行优先使用 LineModel
10. 保持现有 Binding 名称稳定
11. 每次优先只改一个页面或一个 ViewModel
