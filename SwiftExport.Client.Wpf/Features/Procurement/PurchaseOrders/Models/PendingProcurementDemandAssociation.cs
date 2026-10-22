using System.Text.Json;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>保存当前页尚未得到明确结果的完整命令；序列化仅用于客户端冻结负载，不作为业务数据存储。</summary>
public sealed class PendingProcurementDemandAssociation
{
    private readonly string _commandJson;
    /// <summary>冻结采购身份与完整命令，后续页面编辑不能改变原命令。</summary>
    public PendingProcurementDemandAssociation(Guid orderId, AssociateProcurementDemandsCommand command)
    { OrderId = orderId; _commandJson = JsonSerializer.Serialize(command); }
    /// <summary>原命令对应的采购单身份。</summary>
    public Guid OrderId { get; }
    /// <summary>原请求曾出现未知结果，后续拒绝不能证明该请求从未提交。</summary>
    public bool WasUnknown { get; private set; }
    /// <summary>记录未知历史，不改变原命令身份和冻结内容。</summary>
    public void MarkUnknown() => WasUnknown = true;
    /// <summary>每次重试生成独立对象，但保持原命令及贡献身份、版本、原因和数量完全一致。</summary>
    public AssociateProcurementDemandsCommand CreateRequest() => JsonSerializer.Deserialize<AssociateProcurementDemandsCommand>(_commandJson)!;
}
