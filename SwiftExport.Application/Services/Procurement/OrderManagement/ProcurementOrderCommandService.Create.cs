using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;

public sealed partial class ProcurementOrderCommandService
{
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail("创建命令不能为空。"));
        return ExecuteAsync(Guid.Empty, command.CommandId, "Create", command, async () =>
        {
            if (command.OrderDate == default || command.Lines is null || command.Lines.Count is < 1 or > 500 || command.Lines.Any(x => x is null))
                throw new InvalidOperationException("订单日期及1至500个有效采购行必填。");
            await confirmation.ValidatePartiesAsync(command.CustomerId, command.SupplierId, ct);
            var customer = (await repository.GetCustomerAsync(command.CustomerId, ct))!;
            var supplier = await confirmation.RequireSupplierAsync(command.SupplierId, ct);
            var head = new PurchaseOrderHead
            {
                Uid = GuidSequentialGenerator.NewGuid(), ExecutionModel = 1, OriginKind = 2,
                CustomerId = customer.Uid, CustomerCode = customer.CustomerCode, CustomerNameSnapshot = customer.CustomerName,
                SupplierId = supplier.Uid, SupplierCode = supplier.SupplierCode, SupplierName = supplier.SupplierName,
                OrderDate = command.OrderDate, Currency = "CNY", ExecutionRequestId = command.CommandId,
                DemandSourceType = null!, DemandSourceUid = null, DemandSourceNo = null!,
                Remark = ProcurementOrderPolicy.Text(command.Remark, 2000, "采购备注"),
                PackagingRequirements = ProcurementOrderPolicy.Text(command.PackagingRequirements, 2000, "包装要求")
            };
            var lines = new List<PurchaseOrderLine>();
            foreach (var item in command.Lines)
            {
                ProcurementOrderPolicy.Number(item.Quantity, 4, "采购数量", true);
                var product = await repository.GetProductAsync(item.ProductId, ct);
                ProcurementOrderPolicy.RequireProduct(product);
                var unit = await repository.GetUnitAsync(product!.Unit?.Trim() ?? "", ct);
                if (string.IsNullOrWhiteSpace(unit)) throw new InvalidOperationException("产品缺少唯一有效基础单位。");
                var target = await confirmation.RequireWarehouseAsync(item.WarehouseSiteId, ct);
                // 映射仅带商务输入，下面显式建立权威身份和历史快照。
                var line = item.Adapt<PurchaseOrderLine>(mappings);
                line.Uid = GuidSequentialGenerator.NewGuid(); line.PurchaseOrderId = head.Uid; line.LineNumber = lines.Count + 1;
                line.ProductId = product.Uid; line.Sku = product.Sku; line.UnitSnapshot = unit;
                line.Quantity = item.Quantity; line.UnlinkedQty = item.Quantity;
                line.MaterialType = product.MaterialType; line.SupplyType = product.SupplyType;
                line.DeliveryWarehouseSiteId = target.Site.Uid; line.DeliveryWarehouseCodeSnapshot = target.Site.Code;
                line.DeliveryWarehouseNameSnapshot = target.Site.Name; line.DeliveryTargetSupplierId = target.Supplier.Uid;
                line.DeliverySupplierCodeSnapshot = target.Supplier.SupplierCode; line.DeliverySupplierNameSnapshot = target.Supplier.SupplierName;
                line.SupplierSku = ProcurementOrderPolicy.Text(item.SupplierSku, 200, "供应商型号");
                line.DescriptionZh = ProcurementOrderPolicy.Text(item.DescriptionZh ?? product.DescriptionZh, 300, "采购描述");
                line.SubNote = ProcurementOrderPolicy.Text(item.SubNote, 2000, "行备注");
                line.UnitAmount = ProcurementOrderPolicy.Amount(item.Quantity, item.UnitPrice);
                line.TaxAmount = ProcurementOrderPolicy.Amount(item.Quantity, item.TaxPrice);
                line.SourceType = null!; line.SourceNo = null!;
                lines.Add(line);
            }
            var number = await numbers.GetNextDocumentNoAsync(new DocumentNoRequest { DocType = "PO", BusinessDate = head.OrderDate, Operator = user.UserName });
            if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo)) throw new InvalidOperationException("采购单号生成失败。");
            head.OrderNo = number.Data.DocumentNo;
            head.ExpectedDate = lines.Min(x => x.PlannedDeliveryDate);
            foreach (var line in lines) line.OrderNo = head.OrderNo;
            await repository.InsertAsync(head, lines, ct);
            return Result(head, lines);
        }, ct);
    }

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, SaveProcurementOrderCommand command, CancellationToken ct = default)
    {
        if (command is null) return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail("保存命令不能为空。"));
        return ExecuteAsync(uid, command.CommandId, "Edit", command, async () =>
        {
            var head = await RequireHeadAsync(uid, ct);
            var lines = await repository.GetLinesAsync(uid, ct);
            ProcurementOrderPolicy.ApplySave(head, lines, command);
            await repository.SaveAsync(head, lines, ct);
            head.Version++;
            foreach (var line in lines) line.Version++;
            return Result(head, lines);
        }, ct);
    }
}
