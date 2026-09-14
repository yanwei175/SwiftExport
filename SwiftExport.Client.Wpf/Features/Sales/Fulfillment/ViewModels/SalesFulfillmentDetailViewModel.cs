using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Features.Sales.Fulfillment.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;

namespace SwiftExportClient.Features.Sales.Fulfillment.ViewModels;

/// <summary>
/// 保留销售履约 Detail 的文档页签外壳；仅接纳销售订单标识，不读取履约业务数据。
/// </summary>
public sealed partial class SalesFulfillmentDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable
{
    /// <summary>销售履约 Detail 的稳定路由键。</summary>
    public const string RouteKey = "sales-fulfillment-detail";

    /// <summary>初始化只读文档页签外壳。</summary>
    public SalesFulfillmentDetailViewModel()
    {
        Title = "销售履约详情";
        IsReadOnly = true;
    }

    /// <summary>当前外壳关联的销售订单标识，仅用于保持页签身份。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial Guid SalesOrderId { get; set; }

    /// <summary>文档页签标题；没有订单上下文时使用通用标题。</summary>
    public string DocumentTitle => SalesOrderId == Guid.Empty
        ? "销售履约详情"
        : $"销售履约详情 {SalesOrderId:N}";

    /// <summary>空壳页面不维护可编辑状态。</summary>
    public bool HasUnsavedChanges => false;

    /// <summary>
    /// 接纳销售订单打开参数以维持稳定页签身份；无效或缺失参数保持通用空壳。
    /// </summary>
    public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SalesOrderId = parameter is SalesFulfillmentDetailOpenRequest request
            ? request.SalesOrderId
            : Guid.Empty;
        return Task.CompletedTask;
    }

    /// <summary>空壳页面没有未保存数据，始终允许关闭。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}
