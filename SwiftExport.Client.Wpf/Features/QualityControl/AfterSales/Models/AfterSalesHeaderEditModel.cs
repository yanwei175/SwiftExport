using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Models;

/// <summary>售后单头编辑状态；只承载页面输入和脏状态。</summary>
public sealed partial class AfterSalesHeaderEditModel : ObservableObject
{
    private bool _loading;

    /// <summary>建立单头变更跟踪。</summary>
    public AfterSalesHeaderEditModel() => PropertyChanged += (_, args) =>
    {
        if (!_loading && args.PropertyName != nameof(IsDirty)) IsDirty = true;
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
}
