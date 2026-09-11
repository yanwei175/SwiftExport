using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;

/// <summary>客户采购默认配置可编辑行，承载业务字段、选择器状态和行级校验。</summary>
public sealed partial class CustomerPoConfigLineModel : BaseLineModel<CustomerPoConfigDto>
{
    /// <summary>获取客户端稳定行键，用于保存结果原位回填。</summary>
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] public partial Guid? CustomerId { get; set; }
    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? SupplierId { get; set; }
    [ObservableProperty] public partial string SupplierCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string Currency { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? DefaultDiscountRate { get; set; }
    [ObservableProperty] public partial string DiscountRemark { get; set; } = string.Empty;
    [ObservableProperty] public partial string InletHoseType { get; set; } = string.Empty;
    [ObservableProperty] public partial string AeratorType { get; set; } = string.Empty;
    [ObservableProperty] public partial string PalletType { get; set; } = string.Empty;
    [ObservableProperty] public partial string BoxType { get; set; } = string.Empty;
    [ObservableProperty] public partial string LabelType { get; set; } = string.Empty;
    [ObservableProperty] public partial string IMType { get; set; } = string.Empty;
    [ObservableProperty] public partial string CartonType { get; set; } = string.Empty;
    [ObservableProperty] public partial string LogoType { get; set; } = string.Empty;
    [ObservableProperty] public partial string SalesPerson { get; set; } = string.Empty;
    [ObservableProperty] public partial string Purchaser { get; set; } = string.Empty;
    [ObservableProperty] public partial string Note { get; set; } = string.Empty;
    /// <summary>获取或设置客户选择器当前项；该 UI 状态不参与持久化脏跟踪。</summary>
    [ObservableProperty] public partial CustomerDto? SelectedCustomer { get; set; }
    /// <summary>获取或设置供应商选择器当前项；该 UI 状态不参与持久化脏跟踪。</summary>
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }

    /// <summary>校验客户和供应商业务编码均已确认。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(CustomerCode)) return "请选择客户。";
        if (string.IsNullOrWhiteSpace(SupplierCode)) return "请选择供应商。";
        if (CustomerCode.Length > 255) return "客户编码不能超过 255 个字符。";
        if (SupplierCode.Length > 50) return "供应商编码不能超过 50 个字符。";
        if (Currency.Length > 10) return "币种代码不能超过 10 个字符。";
        if (DefaultDiscountRate is < 0m) return "默认折扣率不能小于 0。";
        if (DefaultDiscountRate.HasValue && decimal.Round(DefaultDiscountRate.Value, 6) != DefaultDiscountRate.Value)
            return "默认折扣率最多保留 6 位小数。";
        if (DiscountRemark.Length > 500) return "折扣备注不能超过 500 个字符。";
        if (new[] { InletHoseType, AeratorType, PalletType, BoxType, LabelType, IMType, CartonType, LogoType }.Any(value => value.Length > 50))
            return "进水管、起泡器、托盘、包装盒、标签、说明书、外箱和标志不能超过 50 个字符。";
        if (SalesPerson.Length > 10) return "销售人员不能超过 10 个字符。";
        if (Purchaser.Length > 10) return "采购人员不能超过 10 个字符。";
        return null;
    }

    /// <summary>从契约载入可编辑字段。</summary>
    protected override void LoadFromDto(CustomerPoConfigDto dto) => dto.Adapt(this);
    /// <summary>转换为批量保存及本地基线所需契约。</summary>
    public override CustomerPoConfigDto ToDto() => this.Adapt<CustomerPoConfigDto>();
    /// <summary>排除选择器对象，避免打开编辑器本身产生业务修改。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) =>
        base.IsTrackingIgnoredProperty(propertyName)
        || propertyName is nameof(SelectedCustomer) or nameof(SelectedSupplier);
}
