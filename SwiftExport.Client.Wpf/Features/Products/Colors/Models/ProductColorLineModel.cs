using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.Colors.Models;

/// <summary>维护颜色资料及全局排序号，字段变更由行基类跟踪并进入批量保存。</summary>
public sealed partial class ProductColorLineModel : BaseLineModel<ProductColorDto>, IDataErrorInfo
{
    [ObservableProperty]
    public partial string ColorCodeUnique { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ColorCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ColorNameEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ColorNameZh { get; set; } = string.Empty;

    /// <summary>获取或设置非负排序号；移动行时由页面统一重编号。</summary>
    [ObservableProperty]
    public partial int SortNo { get; set; }

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(ColorCodeUnique) when string.IsNullOrWhiteSpace(ColorCodeUnique) => "唯一颜色代码不能为空。",
        nameof(ColorCode) when string.IsNullOrWhiteSpace(ColorCode) => "颜色代码不能为空。",
        nameof(SortNo) when SortNo < 0 => "排序号不能小于零。",
        _ => string.Empty
    };

    /// <summary>保存前校验必填颜色编码及非负排序号。</summary>
    public override Task<string?> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(ColorCodeUnique))
        {
            return Task.FromResult<string?>("唯一颜色代码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(ColorCode))
        {
            return Task.FromResult<string?>("颜色代码不能为空。");
        }

        return Task.FromResult<string?>(SortNo < 0 ? "排序号不能小于零。" : null);
    }

    protected override void LoadFromDto(ProductColorDto dto)
    {
        dto.Adapt(this);
    }

    public override ProductColorDto ToDto() => this.Adapt<ProductColorDto>();
}
