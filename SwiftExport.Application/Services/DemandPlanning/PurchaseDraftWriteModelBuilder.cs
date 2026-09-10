using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>把已经确认的预览文档转换为待写入实体；不访问数据库。</summary>
public sealed class PurchaseDraftWriteModelBuilder
{
    public PurchaseDraftWriteModel Build(
        PurchaseDraftPreviewDocumentDto document,
        Guid executionRequestId,
        Guid purchaseOrderId,
        string orderNo,
        int firstLineNumber,
        bool createHead)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (executionRequestId == Guid.Empty || purchaseOrderId == Guid.Empty)
            throw new ArgumentException("执行请求和采购单 Uid 不能为空。");
        if (string.IsNullOrWhiteSpace(orderNo))
            throw new ArgumentException("采购单号不能为空。", nameof(orderNo));
        if (firstLineNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(firstLineNumber));

        var result = new PurchaseDraftWriteModel();
        if (createHead)
        {
            result.Head = new PurchaseOrderHead
            {
                Uid = purchaseOrderId,
                Version = 1,
                OrderNo = orderNo,
                SupplierId = document.SupplierId,
                SupplierCode = document.SupplierCode,
                SupplierName = document.SupplierName,
                DemandSourceType = document.SourceType,
                DemandSourceUid = document.SourceUid,
                DemandSourceNo = document.SourceNo,
                ExecutionRequestId = executionRequestId,
                LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
                ConfirmationStatus = ConfirmationStatusEnum.待确认,
                Currency = CurrencyEnum.CNY,
                OrderDate = DateTime.Now,
                ExpectedDate = document.EarliestPlannedDeliveryDate
            };
        }

        var lineNumber = firstLineNumber;
        foreach (var previewLine in document.Lines)
        {
            if (previewLine.Contributions.Count == 0)
                throw new InvalidOperationException("采购预览行必须至少包含一条来源贡献。");
            if (previewLine.Contributions.Sum(x => x.PurchaseQty) != previewLine.PurchaseQty)
                throw new InvalidOperationException("采购预览行数量与来源贡献数量不守恒。");

            var purchaseLineId = GuidSequentialGenerator.NewGuid();
            var singleContribution = previewLine.Contributions.Count == 1
                ? previewLine.Contributions[0]
                : null;
            var line = new PurchaseOrderLine
            {
                Uid = purchaseLineId,
                Version = 1,
                PurchaseOrderId = purchaseOrderId,
                OrderNo = orderNo,
                LineNumber = lineNumber++,
                ProductId = previewLine.ProductId,
                Sku = previewLine.Sku,
                LinkSku = previewLine.LinkSku,
                CustomerSku = previewLine.CustomerSku,
                SupplierSku = previewLine.SupplierSku,
                DescriptionZh = previewLine.Description,
                UnitSnapshot = previewLine.Unit,
                UnitPrice = previewLine.UnitPrice,
                TaxPrice = previewLine.TaxPrice,
                Quantity = previewLine.PurchaseQty,
                UnitAmount = previewLine.UnitPrice * previewLine.PurchaseQty,
                TaxAmount = previewLine.TaxPrice * previewLine.PurchaseQty,
                PlannedDeliveryDate = previewLine.PlannedDeliveryDate,
                DeliveryTargetSupplierId = previewLine.DeliveryTargetSupplierId,
                DeliverySupplierCodeSnapshot = previewLine.DeliveryTargetSupplierCode,
                DeliverySupplierNameSnapshot = previewLine.DeliveryTargetSupplierName,
                SupplyType = SupplyTypeEnums.采购,
                MaterialType = previewLine.MaterialType,
                SourceType = document.SourceType,
                SourceUid = document.SourceUid,
                SourceNo = document.SourceNo,
                SourceLineId = singleContribution?.SourceLineId,
                SourceLineNumber = singleContribution?.SourceLineNumber
            };
            result.Lines.Add(line);

            foreach (var contribution in previewLine.Contributions)
            {
                result.Links.Add(new DemandExecutionLink
                {
                    Uid = GuidSequentialGenerator.NewGuid(),
                    Version = 1,
                    NetDemandId = contribution.NetDemandId,
                    NetDemandAllocationId = null,
                    ExecutionRequestId = executionRequestId,
                    CommandItemId = contribution.CommandItemId,
                    TargetType = DocumentTypeStaticsEnum.PurchaseOrder,
                    TargetHeadId = purchaseOrderId,
                    TargetNo = orderNo,
                    TargetLineId = purchaseLineId,
                    LinkStatus = DemandExecutionLinkStatusEnum.Active,
                    AllocatedQty = contribution.PurchaseQty,
                    DemandRequiredDate = contribution.DemandRequiredDate,
                    PlannedDeliveryDate = contribution.PlannedDeliveryDate
                });
            }
        }

        return result;
    }
}

public sealed class PurchaseDraftWriteModel
{
    public PurchaseOrderHead? Head { get; set; }
    public List<PurchaseOrderLine> Lines { get; } = [];
    public List<DemandExecutionLink> Links { get; } = [];
}
