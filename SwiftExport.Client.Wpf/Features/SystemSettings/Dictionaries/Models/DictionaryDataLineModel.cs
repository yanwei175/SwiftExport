using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Models;

/// <summary>承载字典明细编辑与脏状态；零版本内置项是已加载的默认快照，编辑后按更新提交。</summary>
public sealed partial class DictionaryDataLineModel : BaseLineModel<SysDictDataDto>, IDataErrorInfo
{
    [ObservableProperty]
    public partial string DictCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DataNameEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DataNameZh { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Remark { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int? SortOrder { get; set; }

    [ObservableProperty]
    public partial string CssClass { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSystem { get; set; }

    /// <summary>服务端返回的内置身份事实，用于保护英文代码及启用状态，不参与用户编辑跟踪。</summary>
    [ObservableProperty]
    public partial bool IsBuiltIn { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(DataNameEn) when string.IsNullOrWhiteSpace(DataNameEn) => "英文名称不能为空。",
        nameof(DataNameZh) when string.IsNullOrWhiteSpace(DataNameZh) => "中文名称不能为空。",
        _ => string.Empty
    };

    /// <summary>检查显示名称必填；内置身份及字段保护仍由服务端执行。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(DataNameEn))
        {
            return "英文名称不能为空。";
        }

        if (string.IsNullOrWhiteSpace(DataNameZh))
        {
            return "中文名称不能为空。";
        }

        return null;
    }

    /// <summary>装载服务端快照；基类使 Version=0 的默认项保持干净状态，不当作新增行。</summary>
    protected override void LoadFromDto(SysDictDataDto dto)
    {
        dto.Adapt(this);
    }

    /// <summary>输出当前编辑字段与原身份、版本，供现有批量更新协议保存。</summary>
    public override SysDictDataDto ToDto() => this.Adapt<SysDictDataDto>();

    /// <summary>内置身份仅随服务端快照装载，不是需要保存的用户编辑。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) =>
        base.IsTrackingIgnoredProperty(propertyName) || propertyName == nameof(IsBuiltIn);
}
