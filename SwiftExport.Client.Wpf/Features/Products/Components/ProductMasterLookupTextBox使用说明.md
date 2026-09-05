# 主型号搜索选择控件

`ProductMasterLookupTextBox` 复制 ProductLookupTextBox 的封装，复用公共 Lookup 的 Popup、ListBox、键盘、提交和清空行为。只有候选内容不同：主图、MasterSku、默认中文描述。

本控件不请求 API，也不保存物料。没有新增接口、DTO、仓储或页面接线。

## 数据与选择

调用方注入现有 `IProductMasterApi`，请求 `GetPagedAsync`，把返回的 Items 包装为 `ProductMasterLookupItemModel`。默认取第 1 页、50 条；此控件不提供分页按钮，更多候选通过细化关键词查找。搜索范围沿用主型号分页接口，包括描述、类型、标签及 Neutral 编码。

```csharp
var page = await api.GetPagedAsync(new ProductMasterSearchRequest
{
    Keyword = keyword,
    PageIndex = 1,
    PageSize = 50
}, cancellationToken);
// 先检查 page.Success；不要把失败解释为没有匹配项。
var candidates = page.Data.Items.Select(dto => new ProductMasterLookupItemModel(dto)).ToArray();
```

候选模型的 `Source` 保留完整 `ProductMasterListItemDto`。顶层只读属性包含全部现有字段：身份/版本/审计、MasterSku、DefaultDescriptionEn/Zh、DefaultFeaturesEn/Zh、DefaultPcsPerPackage、DefaultNetWeight/GrossWeight/Cbm、ProductType/Tag、分类、默认供应商、主图和默认主图 Neutral 的两种价格。

这些属性直接读 Source，不是第二份 DTO，也不支持编辑状态或属性变更通知；将其作为查询快照使用，刷新时创建新候选。字符串及价格保留 null；异常响应中的 Data=null 安全显示空内容，装率回退为契约默认值 1，关系 ID 回退为空 Guid。不包含供应商代码、颜色、汇率或完整 Neutral 集合。未来新增字段可先通过 Source 访问。

## XAML 接线示例

```xml
<!-- xmlns:products="clr-namespace:SwiftExportClient.Features.Products.Components" -->
<products:ProductMasterLookupTextBox
    Placeholder="搜索主型号或描述"
    ItemsSource="{Binding MasterCandidates}"
    SelectedProductMaster="{Binding SelectedMaster, Mode=TwoWay}"
    SelectedProductMasterId="{Binding SelectedMasterId, Mode=TwoWay}"
    SearchCommand="{Binding SearchMastersCommand}"
    IsBusy="{Binding IsMasterSearching}"
    SelectionCommittedCommand="{Binding CommitMasterCommand}"
    SelectionClearedCommand="{Binding ClearMasterCommand}" />
```

DataGrid 中，候选和搜索命令通常来自页面 DataContext，提交/清空命令的 Parameter 绑定当前行。由调用方在提交命令中读取 SelectedMaster 的完整资料并回填目标；不要在搜索或候选更换时保存数据。已有记录初始化时，同时提供候选对象，不要只靠一个尚未出现在 ItemsSource 中的 ID 期待控件请求详情。

## 调用方搜索命令示例

以下成员放在调用方的 partial ViewModel 内；`_masterApi`、`_runner` 通过构造注入。示例只说明搜索和释放，不要求为控件另建 Service。页面已有生命周期和错误区时合并到其现有机制，不重复建立。

```csharp
private CancellationTokenSource? _masterSearchCts;
private bool _lookupDisposed;

[ObservableProperty]
public partial IReadOnlyList<ProductMasterLookupItemModel> MasterCandidates { get; set; } = [];
[ObservableProperty]
public partial ProductMasterLookupItemModel? SelectedMaster { get; set; }
[ObservableProperty]
public partial Guid? SelectedMasterId { get; set; }
[ObservableProperty]
public partial bool IsMasterSearching { get; set; }
[ObservableProperty]
public partial string? MasterSearchError { get; set; }

// 允许新查询进入以取消旧查询；不采用写命令的拒绝并发策略。
[RelayCommand(AllowConcurrentExecutions = true)]
private async Task SearchMastersAsync(string? keyword)
{
    if (_lookupDisposed) return;
    var previous = _masterSearchCts;
    var current = new CancellationTokenSource();
    _masterSearchCts = current;
    previous?.Cancel(); // 旧调用在自己的 finally 中释放。
    IsMasterSearching = true;
    MasterSearchError = null;
    try
    {
        var result = await _runner.RunAsync(() => _masterApi.GetPagedAsync(
            new ProductMasterSearchRequest
            {
                Keyword = keyword ?? string.Empty,
                PageIndex = 1,
                PageSize = 50
            }, current.Token), current.Token, "搜索主型号失败");

        // 即使 API 忽略取消，也不能让旧结果覆盖新关键词的候选。
        if (_lookupDisposed || current.IsCancellationRequested ||
            !ReferenceEquals(_masterSearchCts, current)) return;
        if (!result.Success || result.Data is null)
        {
            MasterCandidates = [];
            MasterSearchError = result.Message;
            return;
        }
        MasterCandidates = result.Data.Items
            .Select(dto => new ProductMasterLookupItemModel(dto)).ToArray();
    }
    catch (OperationCanceledException) when (current.IsCancellationRequested)
    {
        // 被新查询替代或页面释放属于正常取消。
    }
    finally
    {
        if (ReferenceEquals(_masterSearchCts, current))
        {
            _masterSearchCts = null;
            if (!_lookupDisposed) IsMasterSearching = false;
        }
        current.Dispose();
    }
}

// 从调用方已有 Dispose/关闭生命周期调用。
private void DisposeMasterLookup()
{
    _lookupDisposed = true;
    var pending = _masterSearchCts;
    _masterSearchCts = null;
    pending?.Cancel();
}
```

`MasterSearchError` 应绑定到调用方可见的错误文本区。明确确认时可读取 `SelectedMaster.Source`，或读取顶层字段；清空命令负责解除目标业务关系及清空调用方选中状态，不在控件中处理。

## 交互和验收

- 默认防抖 300ms、最小长度 1；初始化、候选替换和选择回填不触发搜索。
- 单击进入编辑；空文本可通过 Space/Down 请求候选。选择显示 MasterSku，ID 对应 Uid。
- RequireItemMatch=True、ClearSelectionOnEdit=False；搜索过程中保留已提交选择，Esc/失焦不发布解除关联命令，显式清空按钮才执行清空命令。
- 图片来自分页现成的 MainImagePath，48×48、96px 解码，不创建大图 Tooltip；空图片不额外请求其他业务接口。
- 自动测试：`dotnet test SwiftExport.Client.Wpf.Tests/SwiftExport.Client.Wpf.Tests.csproj --filter FullyQualifiedName~ProductMasterLookupTextBoxTests`。
- 接入业务页面后人工验收 Tab/Shift+Tab、Space/Down、Enter、Esc、清空及焦点返回，检查高对比度、200% 缩放、长中文描述和空图片。当前没有接入页面，不宣称已完成目标页面人工验收。
