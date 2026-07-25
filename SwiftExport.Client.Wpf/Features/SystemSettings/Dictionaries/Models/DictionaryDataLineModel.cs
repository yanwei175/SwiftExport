using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Models;

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

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(DataNameEn) when string.IsNullOrWhiteSpace(DataNameEn) => "英文名称不能为空。",
        nameof(DataNameZh) when string.IsNullOrWhiteSpace(DataNameZh) => "中文名称不能为空。",
        _ => string.Empty
    };

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

    protected override void LoadFromDto(SysDictDataDto dto)
    {
        dto.Adapt(this);
    }

    public override SysDictDataDto ToDto() => this.Adapt<SysDictDataDto>();
}
