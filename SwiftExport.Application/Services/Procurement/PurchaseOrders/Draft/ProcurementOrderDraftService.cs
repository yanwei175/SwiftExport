using SwiftExport.Application.Interfaces.Systems.Numbering;
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
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;

/// <summary>独立采购草稿创建，以及需求来源与独立采购的草稿保存；公共执行器拥有可串行化事务和成功回执。</summary>
public sealed class ProcurementOrderDraftService(
    IProcurementOrderDocumentRepository repository, IProcurementReferenceReadRepository references,
    ProcurementConfirmationBuilder confirmation,
    IDocumentNoService numbers, ICurrentUser user, TypeAdapterConfig mappings,
    BusinessCommandExecutor executor, CommandPipeline pipeline, ProcurementOrderCommandSupport support,
    IForExcelPurchaseOrderRepository importReferences)
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
                IndependentPurchaseOrderInputPolicy.FingerprintInput(command),
                token => CreateInTransactionAsync(command, token), ct, IsolationLevel.Serializable), ct);

    /// <summary>在首次执行的事务中核对采购事实、生成单号、写入草稿和业务审计。</summary>
    private async Task<ProcurementOrderCommandResult> CreateInTransactionAsync(CreateIndependentPurchaseOrderCommand command, CancellationToken ct)
    {
        if (command.OrderDate == default || command.Lines is null || command.Lines.Count is < 1 or > 500 || command.Lines.Any(x => x is null))
            throw new InvalidOperationException("订单日期及1至500个有效采购行必填。");
        await confirmation.ValidatePartiesAsync(command.CustomerId, command.SupplierId, ct);
        var customer = (await references.GetCustomerAsync(command.CustomerId, ct))!;
        var supplier = await confirmation.RequireSupplierAsync(command.SupplierId, ct);
        IndependentPurchaseOrderInputPolicy.ValidateHeader(command);
        // 检查接口仅供预览；正式创建在事务内复核，唯一索引处理并发占号。
        if (await repository.BoNoExistsAsync(command.BoNo, ct))
            throw ProcurementOperationException.Validation(
                "自定义采购单号（BONO）已被未删除的采购订单使用，请修改后重新保存。", "BoNo");
        // 只映射商务快照；系统身份、来源与审计不由输入控制。
        var head = command.Adapt<PurchaseOrderHead>(mappings);
        head.Uid = GuidSequentialGenerator.NewGuid(); head.OriginKind = 2;
        head.CustomerId = customer.Uid; head.CustomerCode = customer.CustomerCode; head.CustomerNameSnapshot = customer.CustomerName;
        head.SupplierId = supplier.Uid; head.SupplierCode = supplier.SupplierCode; head.SupplierName = supplier.SupplierName;
        head.Currency = "CNY"; head.ExecutionRequestId = command.CommandId;
        head.DemandSourceType = null; head.DemandSourceUid = null; head.DemandSourceNo = null;
        var customerSkus = command.Lines.Where(x => !x.IsNonStandard && !string.IsNullOrWhiteSpace(x.CustomerSku))
            .Select(x => IndependentPurchaseOrderInputPolicy.Normalize(x.CustomerSku)).Distinct().ToArray();
        // 正式创建重新读取完整候选，并在既有可串行化事务内锁定关系；检查结果不是保存凭证。
        var matchedProducts = await importReferences.MatchProductsAsync(customer.Uid,customerSkus,ct);
        var lines = new List<PurchaseOrderLine>();
        foreach (var item in command.Lines)
        {
            IndependentPurchaseOrderInputPolicy.ValidateLine(item);
            var line = item.Adapt<PurchaseOrderLine>(mappings);
            line.Uid = GuidSequentialGenerator.NewGuid(); line.PurchaseOrderId = head.Uid; line.LineNumber = lines.Count + 1;
            line.Quantity = item.Quantity; line.UnlinkedQty = item.Quantity;
            if (item.IsNonStandard)
            {
                // 非标身份来自明确输入，编号由服务生成；不伪造产品或供货属性。
                line.ProductId = null; line.Sku = NonStandardSkuGenerator.Create(line.Uid);
                line.UnitSnapshot = await references.GetUnitAsync(item.Unit.Trim(),ct)
                    ?? throw ProcurementOperationException.Validation("非标单位未启用或有歧义。","Lines[].Unit");
                line.MaterialType = null; line.SupplyType = null;
            }
            else
            {
                if (item.ProductId == Guid.Empty)
                    throw ProcurementOperationException.Validation("标准行必须选择真实产品。","Lines[].ProductId");
                var product = await references.GetProductAsync(item.ProductId,ct);
                ProcurementOrderPolicy.RequireProduct(product);
                var productSnapshot = await references.GetProductCandidateAsync(item.ProductId,command.CustomerId,command.SupplierId,ct)
                    ?? throw new InvalidOperationException("产品采购快照读取失败。");
                line.ProductId = product!.Uid; line.Sku = product.Sku;
                line.UnitSnapshot = await references.GetUnitAsync(product.Unit?.Trim() ?? "",ct)
                    ?? throw new InvalidOperationException("产品缺少唯一有效基础单位。");
                line.CustomerSku = productSnapshot.CustomerSku; line.LinkSku = productSnapshot.LinkSku;
                line.SupplierSku = productSnapshot.SupplierSku; line.DescriptionZh = productSnapshot.DescriptionZh;
                line.FinishZh = productSnapshot.FinishZh; line.FeaturesZh = productSnapshot.FeaturesZh;
                line.MaterialType = product.MaterialType; line.SupplyType = product.SupplyType;
                if (item.CustomerSku is not null || item.SelectedCustomerProductId.HasValue)
                {
                    var matched = IndependentPurchaseOrderInputPolicy.Resolve(item.CustomerSku,item.SelectedCustomerProductId,matchedProducts);
                    if (matched.ProductId != item.ProductId)
                        throw ProcurementOperationException.Validation("产品身份与当前客户型号匹配结果不一致。","Lines[].ProductId");
                    line.CustomerSku = matched.CustomerSku; line.LinkSku = matched.LinkSku;
                }
                if (!string.IsNullOrWhiteSpace(item.LinkSku)
                    && IndependentPurchaseOrderInputPolicy.Normalize(item.LinkSku) != IndependentPurchaseOrderInputPolicy.Normalize(line.LinkSku)
                    && !item.AcceptLinkSkuMismatch)
                    throw ProcurementOperationException.Validation("本司型号与系统中性型号不同，必须先确认。","Lines[].LinkSku");
                if (product.MaterialType == MaterialTypesEnum.客供件)
                {
                    if (!item.DeliverySupplierId.HasValue || item.DeliverySupplierId == Guid.Empty)
                        throw new InvalidOperationException("客供件必须选择送达供应商。");
                    var deliverySupplier = await confirmation.RequireSupplierAsync(item.DeliverySupplierId.Value,ct);
                    line.DeliveryTargetSupplierId = deliverySupplier.Uid;
                    line.DeliverySupplierCodeSnapshot = deliverySupplier.SupplierCode;
                    line.DeliverySupplierNameSnapshot = deliverySupplier.SupplierName;
                }
            }
            IndependentPurchaseOrderInputPolicy.ApplyCommercial(line,item);
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
        ProcurementOrderCommandSupport.EnsureWriteSucceeded(await repository.InsertAsync(head, lines, ct));
        var result = ProcurementOrderCommandSupport.Result(head, lines);
        await support.RecordSuccessAsync(command.CommandId, "Create", result, ct);
        return result;
    }

    /// <summary>读取草稿操作所需采购头；不存在时阻断操作。</summary>
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
        ProcurementOrderCommandSupport.EnsureWriteSucceeded(await repository.SaveAsync(head, lines, ct));
        head.Version++;
        foreach (var line in lines) line.Version++;
        var result = ProcurementOrderCommandSupport.Result(head, lines);
        await support.RecordSuccessAsync(command.CommandId, "Edit", result, ct);
        return result;
    }
}
