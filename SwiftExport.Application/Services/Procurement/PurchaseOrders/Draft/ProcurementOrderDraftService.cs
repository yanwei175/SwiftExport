using System.Data;
using Mapster;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;

/// <summary>独立采购草稿的创建与商务保存；公共执行器拥有可串行化事务和成功回执。</summary>
public sealed class ProcurementOrderDraftService(
    IProcurementOrderDocumentRepository repository, IProcurementReferenceReadRepository references,
    ProcurementConfirmationBuilder confirmation,
    IDocumentNoService numbers, ICurrentUser user, TypeAdapterConfig mappings,
    BusinessCommandExecutor executor, CommandPipeline pipeline, ProcurementOrderCommandSupport support)
    : IProcurementOrderDraftService
{
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand command, CancellationToken ct = default) =>
        pipeline.ExecuteAsync<CreateIndependentPurchaseOrderCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            "Procurement.Order.Create", token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Create", token),
            token => ExecuteCreateCommandAsync(command!, token), ct);

    /// <summary>校验命令入口并交给公共执行器；空目标表示订单身份须在首次执行内生成。</summary>
    private Task<ServiceResult<CommandExecutionResult<ProcurementOrderCommandResult>>> ExecuteCreateCommandAsync(
        CreateIndependentPurchaseOrderCommand command, CancellationToken ct) => support.RunAsync(
            () => executor.ExecuteAsync(command.CommandId, "Procurement.Order.Create", null,
                new { command.CustomerId, command.SupplierId, command.OrderDate, command.Remark, command.PackagingRequirements, command.Lines },
                token => CreateInTransactionAsync(command, token), ct, IsolationLevel.Serializable), ct);

    /// <summary>在首次执行的事务中核对采购事实、生成单号、写入草稿和业务审计。</summary>
    private async Task<ProcurementOrderCommandResult> CreateInTransactionAsync(CreateIndependentPurchaseOrderCommand command, CancellationToken ct)
    {
        if (command.OrderDate == default || command.Lines is null || command.Lines.Count is < 1 or > 500 || command.Lines.Any(x => x is null))
            throw new InvalidOperationException("订单日期及1至500个有效采购行必填。");
        await confirmation.ValidatePartiesAsync(command.CustomerId, command.SupplierId, ct);
        var customer = (await references.GetCustomerAsync(command.CustomerId, ct))!;
        var supplier = await confirmation.RequireSupplierAsync(command.SupplierId, ct);
        var head = new PurchaseOrderHead
        {
            Uid = GuidSequentialGenerator.NewGuid(), OriginKind = 2,
            CustomerId = customer.Uid, CustomerCode = customer.CustomerCode, CustomerNameSnapshot = customer.CustomerName,
            SupplierId = supplier.Uid, SupplierCode = supplier.SupplierCode, SupplierName = supplier.SupplierName,
            OrderDate = command.OrderDate, Currency = "CNY", ExecutionRequestId = command.CommandId,
            DemandSourceType = null, DemandSourceUid = null, DemandSourceNo = null,
            Remark = ProcurementOrderPolicy.Text(command.Remark, 2000, "采购备注"),
            PackagingRequirements = ProcurementOrderPolicy.Text(command.PackagingRequirements, 2000, "包装要求")
        };
        var lines = new List<PurchaseOrderLine>();
        foreach (var item in command.Lines)
        {
            ProcurementOrderPolicy.Number(item.Quantity, 4, "采购数量", true);
            if (!item.UnitPrice.HasValue)
                throw ProcurementOperationException.Validation("未税单价不能为空；无报价时请明确输入0。", "Lines[].UnitPrice");
            var product = await references.GetProductAsync(item.ProductId, ct);
            ProcurementOrderPolicy.RequireProduct(product);
            var productSnapshot = await references.GetProductCandidateAsync(item.ProductId, command.CustomerId, command.SupplierId, ct)
                ?? throw new InvalidOperationException("产品采购快照读取失败。");
            var unit = await references.GetUnitAsync(product!.Unit?.Trim() ?? "", ct);
            if (string.IsNullOrWhiteSpace(unit)) throw new InvalidOperationException("产品缺少唯一有效基础单位。");
            // 映射仅带商务输入，下面显式建立权威身份和历史快照。
            var line = item.Adapt<PurchaseOrderLine>(mappings);
            line.Uid = GuidSequentialGenerator.NewGuid(); line.PurchaseOrderId = head.Uid; line.LineNumber = lines.Count + 1;
            line.ProductId = product.Uid; line.Sku = product.Sku; line.UnitSnapshot = unit;
            line.CustomerSku = productSnapshot.CustomerSku; line.LinkSku = productSnapshot.LinkSku;
            line.SupplierSku = productSnapshot.SupplierSku; line.DescriptionZh = productSnapshot.DescriptionZh;
            line.FinishZh = productSnapshot.FinishZh; line.FeaturesZh = productSnapshot.FeaturesZh;
            line.Quantity = item.Quantity; line.UnlinkedQty = item.Quantity;
            line.MaterialType = product.MaterialType; line.SupplyType = product.SupplyType;
            if (product.MaterialType == MaterialTypesEnum.客供件)
            {
                if (!item.DeliverySupplierId.HasValue || item.DeliverySupplierId == Guid.Empty)
                    throw new InvalidOperationException("客供件必须选择送达供应商。");
                var deliverySupplier = await confirmation.RequireSupplierAsync(item.DeliverySupplierId.Value, ct);
                line.DeliveryTargetSupplierId = deliverySupplier.Uid;
                line.DeliverySupplierCodeSnapshot = deliverySupplier.SupplierCode;
                line.DeliverySupplierNameSnapshot = deliverySupplier.SupplierName;
            }
            else
            {
                line.DeliveryTargetSupplierId = null;
                line.DeliverySupplierCodeSnapshot = null;
                line.DeliverySupplierNameSnapshot = null;
            }
            line.SupplierSku = ProcurementOrderPolicy.Text(item.SupplierSku ?? productSnapshot.SupplierSku, 200, "供应商型号");
            line.DescriptionZh = ProcurementOrderPolicy.Text(item.DescriptionZh ?? productSnapshot.DescriptionZh, 300, "采购描述");
            line.SubNote = ProcurementOrderPolicy.Text(item.SubNote, 2000, "行备注");
            line.UnitAmount = ProcurementOrderPolicy.Amount(item.Quantity, item.UnitPrice);
            line.TaxAmount = ProcurementOrderPolicy.Amount(item.Quantity, item.TaxPrice);
            line.SourceType = null; line.SourceNo = null;
            lines.Add(line);
        }
        var number = await numbers.GetNextDocumentNoAsync(new DocumentNoRequest { DocType = "PO", BusinessDate = head.OrderDate, Operator = user.UserName });
        if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo)) throw new InvalidOperationException("采购单号生成失败。");
        head.OrderNo = number.Data.DocumentNo;
        head.ExpectedDate = lines.Min(x => x.PlannedDeliveryDate);
        foreach (var line in lines) line.OrderNo = head.OrderNo;
        await repository.InsertAsync(head, lines, ct);
        var result = ProcurementOrderCommandSupport.Result(head, lines);
        await support.RecordSuccessAsync(command.CommandId, "Create", result, ct);
        return result;
    }

    /// <summary>读取独立采购单，缺失时沿用原有业务阻断结果。</summary>
    private async Task<PurchaseOrderHead> RequireHeadAsync(Guid uid, CancellationToken ct) =>
        await repository.GetHeadAsync(uid, ct) ?? throw new InvalidOperationException("独立采购单不存在。");

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken ct = default) =>
        pipeline.ExecuteAsync<UpdatePurchaseOrderDraftCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            "Procurement.Order.Edit", token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Edit", token),
            token => ExecuteSaveCommandAsync(uid, command!, token), ct);

    /// <summary>把路由身份纳入公共回执目标，格式检查先于命令锁。</summary>
    private Task<ServiceResult<CommandExecutionResult<ProcurementOrderCommandResult>>> ExecuteSaveCommandAsync(
        Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken ct) => support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw ProcurementOperationException.Validation("采购身份不能为空。", "Uid");
            return await executor.ExecuteAsync(command.CommandId, "Procurement.Order.Edit", uid,
                new { command.ExpectedVersion, command.Remark, command.PackagingRequirements, command.DiscountRemark, command.Lines },
                token => SaveInTransactionAsync(uid, command, token), ct, IsolationLevel.Serializable);
        }, ct);

    /// <summary>在可串行化事务中核对头行版本，只保存既有商务白名单与业务审计。</summary>
    private async Task<ProcurementOrderCommandResult> SaveInTransactionAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken ct)
    {
        var head = await RequireHeadAsync(uid, ct);
        var lines = await repository.GetLinesAsync(uid, ct);
        ProcurementOrderPolicy.ApplySave(head, lines, command);
        await repository.SaveAsync(head, lines, ct);
        head.Version++;
        foreach (var line in lines) line.Version++;
        var result = ProcurementOrderCommandSupport.Result(head, lines);
        await support.RecordSuccessAsync(command.CommandId, "Edit", result, ct);
        return result;
    }
}
