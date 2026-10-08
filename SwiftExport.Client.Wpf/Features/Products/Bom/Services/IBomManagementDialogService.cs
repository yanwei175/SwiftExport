using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Bom.Models;

namespace SwiftExportClient.Features.Products.Bom.Services;

/// <summary>为管理页组装绑定、复制来源和结构树弹窗，隔离具体 WPF 视图。</summary>
public interface IBomManagementDialogService
{
    /// <summary>为待建产品收集编号并独立提交绑定，成功后才返回回执。</summary>
    Task<BomBindingDialogResult> BindAsync(BomProductItemModel product, CancellationToken cancellationToken);
    /// <summary>选择具有已保存组成的来源成品，取消不改变目标。</summary>
    Task<BomSourceSelectionResult> SelectSourceAsync(Guid targetId, CancellationToken cancellationToken);
    /// <summary>展示已保存结构的只读树，不提供编辑或保存操作。</summary>
    Task ShowTreeAsync(string sku, IReadOnlyList<BomTreeDto> nodes, CancellationToken cancellationToken);
}
