using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Inventory;
using System.ComponentModel.DataAnnotations;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Models;

/// <summary>承载虚拟仓抽屉的独立编辑状态、字段验证、脏状态和稳定命令标识。</summary>
public sealed partial class WarehouseSiteEditModel : ObservableValidator
{
    private bool _suppressTracking;
    private bool _nameWasEdited;
    private Guid? _commandId;

    [ObservableProperty]
    public partial Guid Uid { get; private set; }

    [ObservableProperty]
    public partial int Version { get; private set; }

    [ObservableProperty]
    public partial bool IsCreateMode { get; private set; }

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "请选择供应商。")]
    public partial WarehouseSupplierLookupDto? SelectedSupplier { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "请输入仓库代码。")]
    [StringLength(100, ErrorMessage = "仓库代码不能超过 100 个字符。")]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "请输入仓库名称。")]
    [StringLength(300, ErrorMessage = "仓库名称不能超过 300 个字符。")]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasChanges { get; private set; }

    /// <summary>抽屉标题，区分新建和改名。</summary>
    public string Title => IsCreateMode ? "新增供应商虚拟仓" : "编辑供应商虚拟仓";

    /// <summary>创建空的新建上下文。</summary>
    public static WarehouseSiteEditModel CreateNew() => new WarehouseSiteEditModel
    {
        _suppressTracking = true,
        IsCreateMode = true,
        IsActive = true
    }.FinishInitialLoad();

    /// <summary>从服务端最新详情创建编辑上下文；代码和供应商只用于只读展示。</summary>
    public static WarehouseSiteEditModel FromDto(WarehouseSiteDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var model = new WarehouseSiteEditModel { _suppressTracking = true };
        model.Uid = dto.Uid;
        model.Version = dto.Version;
        model.IsCreateMode = false;
        model.IsActive = dto.IsActive;
        model.Code = dto.Code;
        model.Name = dto.Name;
        model.SelectedSupplier = dto.SupplierId is { } supplierId
            ? new WarehouseSupplierLookupDto { Uid = supplierId, SupplierCode = dto.SupplierCode, SupplierName = dto.SupplierName }
            : null;
        return model.FinishInitialLoad();
    }

    /// <summary>触发全部字段验证，并返回是否可提交。</summary>
    public bool ValidateForSave()
    {
        ValidateAllProperties();
        return !HasErrors && SelectedSupplier?.Uid != Guid.Empty;
    }

    /// <summary>为当前未改变的用户意图取得稳定命令标识；字段变化后自动生成新意图。</summary>
    public Guid GetOrCreateCommandId() => _commandId ??= Guid.NewGuid();

    /// <summary>在服务端确认成功后接纳返回值并清理脏状态。</summary>
    public void Accept(WarehouseSiteDto dto)
    {
        var loaded = FromDto(dto);
        _suppressTracking = true;
        Uid = loaded.Uid;
        Version = loaded.Version;
        IsCreateMode = loaded.IsCreateMode;
        IsActive = loaded.IsActive;
        SelectedSupplier = loaded.SelectedSupplier;
        Code = loaded.Code;
        Name = loaded.Name;
        HasChanges = false;
        _commandId = null;
        _nameWasEdited = false;
        _suppressTracking = false;
        OnPropertyChanged(nameof(Title));
    }

    partial void OnSelectedSupplierChanged(WarehouseSupplierLookupDto? value)
    {
        MarkChanged();
        if (!IsCreateMode || _nameWasEdited || value is null) return;
        _suppressTracking = true;
        Name = string.IsNullOrWhiteSpace(value.SupplierName) ? string.Empty : value.SupplierName + "虚拟仓";
        _suppressTracking = false;
        MarkChanged();
    }

    partial void OnCodeChanged(string value) => MarkChanged();

    partial void OnNameChanged(string value)
    {
        if (!_suppressTracking) _nameWasEdited = true;
        MarkChanged();
    }

    /// <summary>完成程序化加载并启用后续用户变更跟踪。</summary>
    private WarehouseSiteEditModel FinishInitialLoad()
    {
        _suppressTracking = false;
        HasChanges = false;
        _commandId = null;
        OnPropertyChanged(nameof(Title));
        return this;
    }

    /// <summary>记录真实编辑并使此前尚未完成的命令意图失效。</summary>
    private void MarkChanged()
    {
        if (_suppressTracking) return;
        HasChanges = true;
        _commandId = null;
    }
}
