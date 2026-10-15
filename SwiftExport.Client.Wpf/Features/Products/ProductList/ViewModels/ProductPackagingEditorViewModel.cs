using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>物料详情包装子区块，拥有包装草稿、包材候选和原生编辑桥；提交由父详情统一负责。</summary>
public sealed partial class ProductPackagingEditorViewModel : ObservableObjectEx, IDisposable
{
    private readonly IProductApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService? _cache;
    private CancellationTokenSource? _lookup;
    private bool _disposed;
    private Guid _owner;
    /// <summary>复用物料候选 API 与请求边界，不创建包装专用查询端点。</summary>
    public ProductPackagingEditorViewModel(IProductApi api, IApiCallRunner runner, IReferenceDataCacheService? cache)
    { _api = api; _runner = runner; _cache = cache; Rows.PropertyChanged += OnRowsChanged; }
    /// <summary>当前包装编辑列表及删除池。</summary>
    public ProductPackageListModel Rows { get; } = new();
    /// <summary>所有行和删除池的真实脏状态。</summary>
    public bool HasChanges => Rows.HasChanges;
    /// <summary>字典有效包装选项，保存 DataNameEn 稳定代码。</summary>
    public IReadOnlyList<SysDictDataDto> PackageTypes => _cache?.GetDictionaryValues(DictCodes.ProductPackageType) ?? [];
    /// <summary>仅启用包材候选。</summary>
    public ObservableCollection<ProductLookupDto> MaterialCandidates { get; } = [];
    /// <summary>当前视觉树的原生输入提交桥。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>明确撤销时取消原生事务缓冲，防止旧目标文本再次写入模型。</summary>
    public Action? CancelPendingEdits { get; set; }
    /// <summary>服务端未返回区块时不可编辑，避免旧服务端/未加载状态被当作空包装。</summary>
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    /// <summary>父详情权限和 Busy 状态的编辑投影。</summary>
    [ObservableProperty] public partial bool CanEdit { get; set; }
    /// <summary>包装行选择，单元格模式不允许删除行。</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeleteRowsCommand))] public partial ProductPackageLineModel? SelectedRow { get; set; }
    /// <summary>原生单元格选择模式。</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeleteRowsCommand))] public partial bool IsCellSelectionMode { get; set; }
    /// <summary>本次明确确认的包材候选。</summary>
    [ObservableProperty] public partial ProductLookupDto? SelectedMaterial { get; set; }
    /// <summary>包材查询独立忙碌状态，不接管父详情保存。</summary>
    [ObservableProperty] public partial bool IsLookupBusy { get; private set; }
    /// <summary>包装区块首错或引用提示。</summary>
    [ObservableProperty] public partial string? StatusText { get; private set; }

    /// <summary>接纳父详情完整快照；null 显式表示未加载，不能发起包装写入。</summary>
    public void Load(Guid owner, IReadOnlyList<ProductPackageDto>? rows)
    {
        _lookup?.Cancel(); _owner = owner;
        Rows.LoadFromDtos(rows ?? []); IsLoaded = rows is not null;
        SelectedRow = Rows.Items.FirstOrDefault(); SelectedMaterial = null; MaterialCandidates.Clear();
        StatusText = IsLoaded ? "装率为成品基础单位数；各层可省略，CBM 手工维护。" : "包装规格尚未加载，当前保持原关系。";
        OnPropertyChanged(nameof(PackageTypes)); NotifyCommands();
    }
    /// <summary>首错选择对应行；类型唯一及字典有效性还会由后端复核。</summary>
    public string? Validate()
    {
        if (!IsLoaded || !HasChanges) return null;
        foreach (var row in Rows.Items)
        {
            var error = row.ValidateRow();
            if (error is null && Rows.Items.Count(other => string.Equals(other.PackageType?.Trim(), row.PackageType?.Trim(), StringComparison.OrdinalIgnoreCase)) > 1)
                error = "同一物料不能有重复包装类型。";
            if (error is not null) { SelectedRow = row; return StatusText = $"包装 {row.PackageType}：{error}"; }
        }
        return null;
    }
    /// <summary>只在区块已加载且明确修改时输出增删改。</summary>
    public ProductPackageChangesRequest? CreateChangesRequest() => IsLoaded ? Rows.CreateChangesRequest() : null;
    private bool CanWrite() => !_disposed && CanEdit && IsLoaded;
    private bool CanDelete() => CanWrite() && !IsCellSelectionMode && (Rows.CheckedCount > 0 || SelectedRow is not null);
    partial void OnCanEditChanged(bool value)
    { if (!value) _lookup?.Cancel(); NotifyCommands(); }
    partial void OnIsLoadedChanged(bool value) => NotifyCommands();
    /// <summary>新增包装采用 Carton 初始类型与正整数一，不自动建立缺失层级。</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void AddRow()
    {
        if (!CanWrite() || CommitPendingEdits?.Invoke() == false) return;
        SelectedRow = Rows.AddNew(new ProductPackageDto { OwnerProductId = _owner, IsPackagingMaterialValid = true,
            Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 1 } });
    }
    /// <summary>行删除进入既有删除池；单元格选择和只读状态不执行删除。</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteRows()
    {
        if (!CanDelete() || CommitPendingEdits?.Invoke() == false) return;
        var selected = Rows.CheckedCount > 0 ? Rows.CheckedItems.ToArray() : [SelectedRow!];
        foreach (var row in selected) Rows.Remove(row);
        SelectedRow = Rows.Items.FirstOrDefault();
    }
    /// <summary>仅撤销包装子区块，不丢弃资料或标签草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public void RejectChanges()
    { if (!CanWrite()) return; CancelPendingEdits?.Invoke(); Rows.RejectChanges(); SelectedRow = Rows.Items.FirstOrDefault(); StatusText = "包装修改已撤销。"; }
    /// <summary>后台只返回启用包材；晚到、取消或失活查询都不得覆盖新候选。</summary>
    [RelayCommand(CanExecute = nameof(CanWrite), AllowConcurrentExecutions = true)]
    private async Task SearchMaterialsAsync(string? keyword)
    {
        if (!CanWrite()) return;
        _lookup?.Cancel(); using var lookup = new CancellationTokenSource(); _lookup = lookup; IsLookupBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchLookupAsync(new ProductLookupRequest
            { Keyword = keyword, MaterialType = MaterialTypesEnum.包材, ActiveOnly = true, Limit = 30 }, lookup.Token), lookup.Token, "查询包材失败");
            if (_disposed || lookup.IsCancellationRequested || !ReferenceEquals(_lookup, lookup) || !CanWrite()) return;
            MaterialCandidates.Clear();
            if (!result.Success) { StatusText = result.Message; return; }
            foreach (var row in result.Data ?? []) if (row.MaterialType == MaterialTypesEnum.包材) MaterialCandidates.Add(row);
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested) { }
        catch (Exception exception)
        { System.Diagnostics.Trace.TraceError("包装包材查询异常：{0}", exception); if (!_disposed && ReferenceEquals(_lookup, lookup)) StatusText = "包材查询未完成，当前选择已保留。"; }
        finally { if (ReferenceEquals(_lookup, lookup)) { _lookup = null; IsLookupBusy = false; } }
    }
    /// <summary>只接纳包材身份和只读展示，不复制候选自重、尺寸或改写包材档案。</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void CommitMaterial(ProductPackageLineModel? row)
    {
        if (!CanWrite() || row is null || SelectedMaterial is null || SelectedMaterial.MaterialType != MaterialTypesEnum.包材) return;
        row.PackagingMaterialId = SelectedMaterial.Uid; row.PackagingMaterialSku = SelectedMaterial.Sku;
        row.PackagingMaterialDescription = SelectedMaterial.DescriptionZh; row.PackagingMaterialUnit = SelectedMaterial.Unit; row.IsPackagingMaterialValid = true;
    }
    /// <summary>显式解除引用，包装结果值保持不变。</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void ClearMaterial(ProductPackageLineModel? row)
    {
        if (!CanWrite() || row is null) return;
        row.PackagingMaterialId = null; row.PackagingMaterialSku = null; row.PackagingMaterialDescription = null;
        row.PackagingMaterialUnit = null; row.IsPackagingMaterialValid = true;
    }
    private void OnRowsChanged(object? sender, PropertyChangedEventArgs args) { OnPropertyChanged(nameof(HasChanges)); NotifyCommands(); }
    private void NotifyCommands()
    { AddRowCommand.NotifyCanExecuteChanged(); DeleteRowsCommand.NotifyCanExecuteChanged(); RejectChangesCommand.NotifyCanExecuteChanged(); SearchMaterialsCommand.NotifyCanExecuteChanged(); CommitMaterialCommand.NotifyCanExecuteChanged(); ClearMaterialCommand.NotifyCanExecuteChanged(); }
    /// <summary>解除子模型订阅并取消候选查询，父文档释放后不再回写。</summary>
    public void Dispose()
    { if (_disposed) return; _disposed = true; _lookup?.Cancel(); Rows.PropertyChanged -= OnRowsChanged; CommitPendingEdits = null; CancelPendingEdits = null; }
}
