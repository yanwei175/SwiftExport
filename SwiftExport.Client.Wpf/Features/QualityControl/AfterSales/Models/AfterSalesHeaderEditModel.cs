using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Models;

/// <summary>售后单头编辑状态；只承载页面输入和脏状态。</summary>
public sealed partial class AfterSalesHeaderEditModel : ObservableObject
{
    /// <summary>缓存中的整单责任供应商选择。</summary>
    [ObservableProperty] public partial SupplierDto? SelectedResponsibleSupplier { get; set; }

    /// <summary>恢复供应商显示，不覆盖业务身份，也不产生脏状态。</summary>
    public void SynchronizeSupplier(SupplierDto? supplier)
    {
        var loading=_loading;
        _loading=true;
        SelectedResponsibleSupplier=supplier;
        _loading=loading;
    }

    partial void OnSelectedResponsibleSupplierChanged(SupplierDto? value)
    {
        if(!_loading) ResponsibleSupplierId=value?.Uid;
    }

    /// <summary>验证整单投诉和成本，返回稳定的单头字段路径。</summary>
    public string? Validate(bool forConfirmation)
    {
        if(string.IsNullOrWhiteSpace(IssueDescription)) return "Header.IssueDescription：投诉描述不能为空。";
        if(CustomerComplaintNo?.Trim().Length>100) return "Header.CustomerComplaintNo：客户投诉单号不能超过100字。";
        if(ServiceAmountCny < 0 || ServiceAmountCny > 9999999999999999.99m
            || decimal.Round(ServiceAmountCny ?? 0,2)!=(ServiceAmountCny ?? 0))
            return "Header.ServiceAmountCny：整单人民币成本必须非负，最多十六位整数、两位小数。";
        if(forConfirmation && !ResponsibleSupplierId.HasValue) return "Header.ResponsibleSupplierId：请选择责任供应商。";
        if(forConfirmation && !ServiceAmountCny.HasValue) return "Header.ServiceAmountCny：请确定整单人民币成本，免费填0。";
        return null;
    }

    /// <summary>问题产品图片，仅用于页面显示。</summary>
    [ObservableProperty] public partial string? ImagePath { get; set; }
    /// <summary>本机投诉资料文件夹，作为普通单头文本提交。</summary>
    [ObservableProperty] public partial string? LocalFileFolder { get; set; }

    private bool _loading;

    /// <summary>建立单头变更跟踪。</summary>
    public AfterSalesHeaderEditModel() => PropertyChanged += (_, args) =>
    {
        if (!_loading && args.PropertyName is not (nameof(IsDirty) or nameof(ImagePath))) IsDirty = true;
    };

    [ObservableProperty] public partial Guid Uid { get; set; }
    [ObservableProperty] public partial int Version { get; set; } = 1;
    [ObservableProperty] public partial string AfterSalesNo { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid CustomerId { get; set; }
    [ObservableProperty] public partial string CustomerCodeSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerNameSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime AcceptedDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial Guid? OwnerId { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedCompletionDate { get; set; }
    [ObservableProperty] public partial string Remark { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsDirty { get; private set; }

    /// <summary>用服务端详情替换编辑基线。</summary>
    public void Load(AfterSalesHeaderDto dto)
    {
        _loading = true;
        dto.Adapt(this);
        _loading = false;
        IsDirty = false;
    }

    /// <summary>生成服务端允许写入的单头事实。</summary>
    public AfterSalesEditHeaderDto ToDto() => this.Adapt<AfterSalesEditHeaderDto>();

    /// <summary>接纳当前值为已保存基线。</summary>
    public void AcceptChanges() => IsDirty = false;
    [ObservableProperty] public partial Guid? OriginalSalesOrderLineId { get; set; }
    /// <summary>获取或设置客户侧投诉单号，清空后按空值保存。</summary>
    [ObservableProperty] public partial string? CustomerComplaintNo { get; set; }
    [ObservableProperty] public partial Guid? ResponsibleSupplierId { get; set; }
    [ObservableProperty] public partial Guid? IssueProductId { get; set; }
    [ObservableProperty] public partial string IssueSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string IssueCustomerSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string IssueLinkSkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial string IssueDescription { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? QualityCategoryId { get; set; }
    [ObservableProperty] public partial string CauseAnalysis { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? RequiredDate { get; set; }
    [ObservableProperty] public partial decimal? ServiceAmountCny { get; set; }
    [ObservableProperty] public partial DateTime? ReturnedReceivedDate { get; set; }
    [ObservableProperty] public partial string TreatmentDescription { get; set; } = string.Empty;

}
