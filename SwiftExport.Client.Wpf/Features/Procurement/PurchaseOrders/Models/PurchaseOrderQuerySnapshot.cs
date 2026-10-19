using System.Text.Json;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>冻结采购管理已应用条件，避免翻页刷新混入待输入条件。</summary>
public sealed partial class PurchaseOrderQuerySnapshot(PurchaseOrderManagerSearchRequest request)
{
    private readonly string _json = JsonSerializer.Serialize(request);
    /// <summary>每次查询独立复制全部集合及条件，只替换本次目标页。</summary>
    public PurchaseOrderManagerSearchRequest ForPage(int page, int size)
    {
        var request = JsonSerializer.Deserialize<PurchaseOrderManagerSearchRequest>(_json)!;
        request.PageIndex = Math.Max(1, page); request.PageSize = Math.Max(1, size); return request;
    }
}
