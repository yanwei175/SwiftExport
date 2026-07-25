using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Models;

public sealed partial class DictionaryTypeEditModel : BaseLineModel<SysDictTypeDto>, IDataErrorInfo
{
    private bool _isProtected;

    [ObservableProperty]
    public partial string DictCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DictName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSystem { get; set; }

    [ObservableProperty]
    public partial string Remark { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Guid? ParentId { get; set; }

    [ObservableProperty]
    public partial int SortOrder { get; set; }

    [ObservableProperty]
    public partial string TargetViewKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GroupCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GroupName { get; set; } = string.Empty;

    public bool IsProtected => _isProtected;

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(DictCode) when string.IsNullOrWhiteSpace(DictCode) => "字典编码不能为空。",
        nameof(DictName) when string.IsNullOrWhiteSpace(DictName) => "字典名称不能为空。",
        _ => string.Empty
    };

    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(DictCode))
        {
            return "字典编码不能为空。";
        }

        if (string.IsNullOrWhiteSpace(DictName))
        {
            return "字典名称不能为空。";
        }

        return null;
    }

    protected override bool IsTrackingIgnoredProperty(string? propertyName)
    {
        return base.IsTrackingIgnoredProperty(propertyName)
            || propertyName == nameof(IsProtected);
    }

    protected override void LoadFromDto(SysDictTypeDto dto)
    {
        dto.Adapt(this);
        _isProtected = dto.IsSystem
            || DictCodes.DictCodeList.Contains(dto.DictCode, StringComparer.OrdinalIgnoreCase);
    }

    public override SysDictTypeDto ToDto() => this.Adapt<SysDictTypeDto>();
}
