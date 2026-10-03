using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.Customers.PoConfigs;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand;

/// <summary>需求转采购预览用例；只读构造预览并为来源事务复核锁定事实。</summary>
public sealed class PurchaseDraftPreviewService(
    ServiceBaseContext context,
    IMaterialDemandQueryRepository netDemandRepository,
    IPurchaseOrderDraftRepository purchaseDraftRepository,
    IProductRepository productRepository,
    ICustomerRepository customerRepository,
    ICustomerPoConfigRepository customerPoConfigRepository,
    ISupplierRepository supplierRepository,
    ISupplierProductRepository supplierProductRepository,
    ISalesOrderHeadRepository salesOrderHeadRepository,
    PurchaseDraftPreviewBuilder previewBuilder) : IPurchaseDraftPreviewService
{
    private readonly ILogger<PurchaseDraftPreviewService> _logger =
        context.LoggerFactory.CreateLogger<PurchaseDraftPreviewService>();

    /// <inheritdoc/>
    public async Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(
        PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidatePreviewRequest(request);
            var workspaceRows = await LoadPreviewWorkspaceRowsAsync(request, cancellationToken);
            var buildRequest = await BuildReadOnlyPreviewRequestAsync(request, workspaceRows, cancellationToken);
            return ServiceResult<PurchaseDraftPreviewResponse>.Ok(previewBuilder.Build(buildRequest));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (PurchaseDraftException ex)
        {
            return ServiceResult<PurchaseDraftPreviewResponse>.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "采购草稿预览失败。");
            return ServiceResult<PurchaseDraftPreviewResponse>.Fail("采购草稿预览失败。");
        }
    }

    /// <summary>按物料需求身份读取预览事实，并补齐同来源行以保留客供件提示语义。</summary>
    private async Task<IReadOnlyList<MaterialDemandManagerItemDto>> LoadPreviewWorkspaceRowsAsync(
        PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick)
        {
            var page = await netDemandRepository.SearchManagerAsync(new MaterialDemandManagerSearchRequest
            {
                SourceUid = request.SourceUid,
                SourceTypes = new List<string> { request.SourceType },
                PageIndex = 1,
                PageSize = 200
            }, cancellationToken);
            var sourceRows = page.Items;
            if (sourceRows.Count == 0)
                throw new PurchaseDraftException("SOURCE_NOT_FOUND", "来源单没有活动物料需求。");
            if (request.Items.Count > 0)
            {
                var rowByDemand = sourceRows.ToDictionary(x => x.Uid);
                foreach (var item in request.Items)
                {
                    if (!rowByDemand.TryGetValue(item.NetDemandId, out var row)
                        || row.Version != item.ExpectedVersion
                        || row.RemainingQty != item.ExpectedRemainingQty)
                        throw new PurchaseDraftException("PREVIEW_FACT_CHANGED",
                            "一键预览包含不属于当前来源或已经变化的物料需求。");
                }
            }
            return sourceRows;
        }

        var selectedRows = new List<MaterialDemandManagerItemDto>();
        foreach (var item in request.Items)
        {
            var page = await netDemandRepository.SearchManagerAsync(new MaterialDemandManagerSearchRequest
            {
                NetDemandIds = new List<Guid> { item.NetDemandId },
                PageIndex = 1,
                PageSize = 1
            }, cancellationToken);
            var row = page.Items.SingleOrDefault()
                ?? throw new PurchaseDraftException("NET_DEMAND_NOT_FOUND", "选择的活动物料需求不存在。");
            if (row.Version != item.ExpectedVersion
                || row.RemainingQty != item.ExpectedRemainingQty)
                throw new PurchaseDraftException("PREVIEW_FACT_CHANGED", "物料需求版本或剩余数量已变化，请刷新后重试。");
            selectedRows.Add(row);
        }

        var rows = new Dictionary<Guid, MaterialDemandManagerItemDto>();
        foreach (var sourceUid in selectedRows.Select(x => x.SourceUid).Distinct())
        {
            var page = await netDemandRepository.SearchManagerAsync(new MaterialDemandManagerSearchRequest
            {
                SourceUid = sourceUid,
                PageIndex = 1,
                PageSize = 200
            }, cancellationToken);
            foreach (var row in page.Items) rows[row.Uid] = row;
        }
        foreach (var row in selectedRows) rows[row.Uid] = row;
        return rows.Values.ToList();
    }

    /// <summary>以工作台当前行、供应商和安全追加候选构造只读预览输入。</summary>
    private async Task<PurchaseDraftPreviewBuildRequest> BuildReadOnlyPreviewRequestAsync(
        PurchaseDraftPreviewRequest request,
        IReadOnlyList<MaterialDemandManagerItemDto> workspaceRows,
        CancellationToken cancellationToken)
    {
        var itemByDemand = request.Items.ToDictionary(x => x.NetDemandId);
        var supplierIds = workspaceRows.SelectMany(row =>
        {
            itemByDemand.TryGetValue(row.Uid, out var item);
            var deliverySupplierId = item is null
                ? row.SuggestedDeliverySupplierId
                : item.DeliveryTargetSupplierId;
            return new[] { item?.SupplierId ?? row.SuggestedPurchaseSupplierId, deliverySupplierId };
        }).Where(x => x.HasValue && x.Value != Guid.Empty).Select(x => x!.Value).Distinct().ToArray();
        var suppliers = (await supplierRepository.GetByIdListAsync(supplierIds)).ToDictionary(x => x.Uid);
        var purchaseRows = workspaceRows.Where(x => ResolveExecutionType(x.SupplyType) == DemandExecutionTypeEnum.Purchase).ToList();
        var purchaseData = await LoadPurchaseDataAsync(purchaseRows.Select(row =>
        {
            itemByDemand.TryGetValue(row.Uid, out var item);
            return (row.ProductId, (Guid?)(item?.SupplierId ?? row.SuggestedPurchaseSupplierId));
        }));
        var candidates = new List<PurchaseDraftPreviewCandidate>();
        foreach (var row in workspaceRows)
        {
            itemByDemand.TryGetValue(row.Uid, out var item);
            var selected = request.EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick || item is not null;
            var commandItemId = item?.CommandItemId
                ?? (selected && ResolveExecutionType(row.SupplyType) == DemandExecutionTypeEnum.Purchase
                    ? GuidSequentialGenerator.NewGuid() : Guid.Empty);
            var purchaseSupplierId = item?.SupplierId ?? row.SuggestedPurchaseSupplierId;
            purchaseData.TryGetValue((row.ProductId, purchaseSupplierId), out var data);
            candidates.Add(MapWorkspaceCandidate(row, item, commandItemId, selected, data, suppliers));
        }

        var selectedSupplierIds = candidates
            .Where(x => x.IsSelected && x.PurchaseSupplierId.HasValue)
            .Select(x => x.PurchaseSupplierId!.Value)
            .Distinct()
            .ToArray();
        var safeAppend = new List<PurchaseDraftSafeAppendCandidate>();
        if (request.EntryMode != PurchaseDraftEntryModeEnum.SourceOneClick)
        {
            foreach (var source in candidates.Where(x => x.IsSelected)
                         .Select(x => (x.SourceType, x.SourceUid)).Distinct())
            {
                var rows = await purchaseDraftRepository.GetSafeAppendCandidatesAsync(
                    source.SourceType, source.SourceUid, selectedSupplierIds, cancellationToken);
                safeAppend.AddRange(rows.Select(x => new PurchaseDraftSafeAppendCandidate
                {
                    PurchaseOrderId = x.PurchaseOrderId,
                    Version = x.Version,
                    OrderNo = x.OrderNo,
                    SourceType = x.SourceType,
                    SourceUid = x.SourceUid,
                    SupplierId = x.SupplierId,
                    UntaxedTotalAmount = x.UntaxedTotalAmount,
                    TaxIncludedTotalAmount = x.TaxIncludedTotalAmount
                }));
            }
        }

        await AttachExistingHeadersAsync(safeAppend, cancellationToken);
        var mappedTargets = request.Targets.Select(MapTarget).ToList();
        var headers = await BuildHeaderFactsAsync(candidates, mappedTargets, withUpdLock: false, cancellationToken);

        var selectedDocumentKeys = candidates
            .Where(x => x.IsSelected && x.ExecutionType == DemandExecutionTypeEnum.Purchase
                && x.PurchaseSupplierId.HasValue)
            .Select(x => (SourceType: x.SourceType.Trim().ToUpperInvariant(), x.SourceUid,
                SupplierId: x.PurchaseSupplierId!.Value))
            .ToHashSet();
        foreach (var target in request.Targets)
        {
            var key = (target.SourceType.Trim().ToUpperInvariant(), target.SourceUid, target.SupplierId);
            if (!selectedDocumentKeys.Contains(key))
                throw new PurchaseDraftException("INVALID_TARGET", "采购草稿目标不属于本次选中的来源和供应商。");
            if (target.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting
                && !safeAppend.Any(x => x.PurchaseOrderId == target.ExistingPurchaseOrderId
                    && x.Version == target.ExpectedPurchaseOrderVersion
                    && x.SourceUid == target.SourceUid
                    && x.SupplierId == target.SupplierId
                    && string.Equals(x.SourceType, target.SourceType, StringComparison.OrdinalIgnoreCase)))
                throw new PurchaseDraftException("APPEND_TARGET_UNSAFE", "目标采购草稿已不满足安全追加条件。");
        }

        return new PurchaseDraftPreviewBuildRequest
        {
            ExecutionRequestId = request.ExecutionRequestId,
            EntryMode = request.EntryMode,
            LineMode = request.LineMode,
            SourceType = request.SourceType,
            SourceUid = request.SourceUid,
            Candidates = candidates,
            Targets = mappedTargets,
            SafeAppendCandidates = safeAppend,
            Headers = headers
        };
    }

    /// <summary>在来源事务内依据已锁定的需求和 Link 重新构造预览事实，不提交或释放事务。</summary>
    internal async Task<PurchaseDraftPreviewBuildRequest> BuildLockedPreviewRequestAsync(
        CreatePurchaseDraftsCommand command,
        CreatePurchaseDraftSourceCommand source,
        IReadOnlyList<Core.Entities.DemandPlanning.GrossDemand> grossDemands,
        IReadOnlyList<NetDemand> netDemands,
        IReadOnlyList<PurchaseDemandCommitmentDto> activeLinks,
        CancellationToken cancellationToken)
    {
        var itemByDemand = source.Items.ToDictionary(x => x.NetDemandId);
        var grossById = grossDemands.ToDictionary(x => x.Uid);
        var netById = netDemands.ToDictionary(x => x.Uid);
        var allocatedByDemand = activeLinks
            .GroupBy(x => x.NetDemandId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.AllocatedQty));

        var supplierIds = source.Items
            .SelectMany(x => new Guid?[] { x.SupplierId, x.DeliveryTargetSupplierId })
            .Concat(netDemands.Select(x => x.SuggestedPurchaseSupplierId))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var suppliers = (await supplierRepository.GetByIdListAsync(supplierIds, withUpdLock: true))
            .ToDictionary(x => x.Uid);
        var customerIds = grossDemands.Where(x => x.CustomerId.HasValue)
            .Select(x => x.CustomerId!.Value).Distinct().ToArray();
        var customers = (await customerRepository.GetByIdListAsync(customerIds, withUpdLock: true))
            .ToDictionary(x => x.Uid);
        var productIds = netDemands.Where(x => x.ProductId.HasValue).Select(x => x.ProductId!.Value).Distinct().ToArray();
        var products = (await productRepository.GetByIdListAsync(productIds, withUpdLock: true))
            .ToDictionary(x => x.Uid);
        var purchaseData = await LoadPurchaseDataAsync(netDemands.Select(net =>
        {
            itemByDemand.TryGetValue(net.Uid, out var item);
            return (net.ProductId, (Guid?)(item?.SupplierId ?? net.SuggestedPurchaseSupplierId));
        }));

        var candidates = new List<PurchaseDraftPreviewCandidate>();
        foreach (var net in netDemands.OrderBy(x => x.LineNumber).ThenBy(x => x.Uid))
        {
            var gross = grossById[net.GrossDemandId];
            customers.TryGetValue(gross.CustomerId ?? Guid.Empty, out var customer);
            itemByDemand.TryGetValue(net.Uid, out var item);
            var allocatedQty = allocatedByDemand.GetValueOrDefault(net.Uid);
            var remainingQty = net.NetQty - allocatedQty;
            var purchaseSupplierId = item?.SupplierId ?? net.SuggestedPurchaseSupplierId;
            var deliveryTarget = net.DeliveryTargetNetDemandId.HasValue
                ? netById.GetValueOrDefault(net.DeliveryTargetNetDemandId.Value)
                : null;
            // 命令项存在时，空送达方表示采购尚未完成实际选择，不能再次回退计划建议绕过预览阻断。
            var deliverySupplierId = item is null
                ? deliveryTarget?.SuggestedPurchaseSupplierId
                : item.DeliveryTargetSupplierId;
            suppliers.TryGetValue(purchaseSupplierId ?? Guid.Empty, out var purchaseSupplier);
            suppliers.TryGetValue(deliverySupplierId ?? Guid.Empty, out var deliverySupplier);
            var deliveryTargetValid = net.DeliveryMode != DemandDeliveryModeEnum.ParentExecutionSupplier
                || (deliveryTarget is not null && deliverySupplier?.IsActive == true);
            purchaseData.TryGetValue((net.ProductId, purchaseSupplierId), out var data);
            Product? product = null;
            if (net.ProductId.HasValue)
                products.TryGetValue(net.ProductId.Value, out product);
            candidates.Add(new PurchaseDraftPreviewCandidate
            {
                CommandItemId = item?.CommandItemId ?? Guid.Empty,
                NetDemandId = net.Uid,
                NetDemandVersion = net.Version,
                SourceType = gross.SourceType,
                SourceUid = gross.SourceUid,
                SourceNo = gross.SourceNo,
                SourceLineId = gross.SourceLineId,
                SourceLineNumber = gross.SourceLineNumber,
                CustomerId = gross.CustomerId,
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                CustomerName = customer?.CustomerName ?? string.Empty,
                IsSelected = item is not null,
                IsCustomerSupplied = net.DeliveryMode == DemandDeliveryModeEnum.ParentExecutionSupplier,
                IsPlanningFactValid = net.NetQty > 0 && remainingQty >= 0,
                ExecutionType = ResolveExecutionType(net.SupplyTypeSnapshot),
                RemainingQty = remainingQty,
                RequestedPurchaseQty = item?.PurchaseQty,
                DemandRequiredDate = net.RequiredDate,
                PlannedDeliveryDate = item is null ? net.RequiredDate : item.PlannedDeliveryDate,
                ProductId = net.ProductId,
                Sku = net.SkuSnapshot,
                LinkSku = product?.LinkSku ?? gross.LinkSkuSnapshot ?? string.Empty,
                CustomerSku = net.CustomerSkuSnapshot ?? string.Empty,
                Description = net.DescriptionSnapshot ?? string.Empty,
                Color = net.ColorSnapshot ?? string.Empty,
                Specification = net.SpecificationSnapshot ?? string.Empty,
                ImagePath = product?.MainImage ?? string.Empty,
                MaterialType = net.MaterialTypeSnapshot ?? string.Empty,
                Unit = EffectiveUnit(net.UnitSnapshot),
                QuantityScale = 4,
                PurchaseSupplierId = purchaseSupplierId,
                PurchaseSupplierIsActive = purchaseSupplier?.IsActive == true,
                PurchaseSupplierCode = purchaseSupplier?.SupplierCode ?? string.Empty,
                PurchaseSupplierName = purchaseSupplier?.SupplierName ?? string.Empty,
                DeliveryTargetNetDemandId = net.DeliveryTargetNetDemandId,
                DeliveryTargetSupplierId = deliverySupplierId,
                DeliveryTargetIsValid = deliveryTargetValid,
                DeliveryTargetSupplierCode = deliverySupplier?.SupplierCode ?? string.Empty,
                DeliveryTargetSupplierName = deliverySupplier?.SupplierName ?? string.Empty,
                HasSupplierProduct = data is not null,
                SupplierSku = data?.SupplierSku ?? string.Empty,
                UnitPrice = item is null ? data?.UnitPrice ?? 0m : item.UnitPrice,
                TaxPrice = item is null ? data?.TaxPrice ?? 0m : item.TaxPrice
            });
        }

        ValidateLockedItems(command.EntryMode, source, candidates);
        var safeAppendRows = command.EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick
            ? Array.Empty<SafePurchaseDraftCandidateDto>()
            : await purchaseDraftRepository.GetSafeAppendCandidatesAsync(
                source.SourceType,
                source.SourceUid,
                source.Targets.Select(x => x.SupplierId),
                cancellationToken);
        var safeAppend = safeAppendRows.Select(x => new PurchaseDraftSafeAppendCandidate
        {
            PurchaseOrderId = x.PurchaseOrderId,
            Version = x.Version,
            OrderNo = x.OrderNo,
            SourceType = x.SourceType,
            SourceUid = x.SourceUid,
            SupplierId = x.SupplierId,
            UntaxedTotalAmount = x.UntaxedTotalAmount,
            TaxIncludedTotalAmount = x.TaxIncludedTotalAmount
        }).ToList();
        await AttachExistingHeadersAsync(safeAppend, cancellationToken);
        var mappedTargets = source.Targets.Select(x => new PurchaseDraftPreviewBuildTarget
        {
            SourceType = source.SourceType,
            SourceUid = source.SourceUid,
            SupplierId = x.SupplierId,
            TargetMode = x.TargetMode,
            ExistingPurchaseOrderId = x.ExistingPurchaseOrderId,
            ExpectedPurchaseOrderVersion = x.ExpectedPurchaseOrderVersion
        }).ToList();
        var headers = await BuildHeaderFactsAsync(candidates, mappedTargets, withUpdLock: true, cancellationToken);
        return new PurchaseDraftPreviewBuildRequest
        {
            ExecutionRequestId = command.ExecutionRequestId,
            EntryMode = command.EntryMode,
            LineMode = source.LineMode,
            SourceType = source.SourceType,
            SourceUid = source.SourceUid,
            Candidates = candidates,
            Targets = mappedTargets,
            SafeAppendCandidates = safeAppend,
            Headers = headers
        };
    }

    /// <summary>为每个新建单据组合读取一次客户配置，并组合销售来源与主数据形成可信单头。</summary>
    private async Task<IReadOnlyList<PurchaseDraftPreviewHeaderFact>> BuildHeaderFactsAsync(
        IReadOnlyList<PurchaseDraftPreviewCandidate> candidates,
        IReadOnlyList<PurchaseDraftPreviewBuildTarget> targets,
        bool withUpdLock,
        CancellationToken cancellationToken)
    {
        var documents = candidates
            .Where(x => x.IsSelected && x.ExecutionType == DemandExecutionTypeEnum.Purchase
                && x.PurchaseSupplierId.HasValue && x.PurchaseSupplierId.Value != Guid.Empty)
            .Where(x => targets.FirstOrDefault(target => target.SourceUid == x.SourceUid
                && string.Equals(target.SourceType, x.SourceType, StringComparison.OrdinalIgnoreCase)
                && target.SupplierId == x.PurchaseSupplierId.GetValueOrDefault())?.TargetMode
                != PurchaseDraftTargetModeEnum.AppendExisting)
            .GroupBy(x => new
            {
                SourceType = x.SourceType.Trim().ToUpperInvariant(),
                x.SourceUid,
                SupplierId = x.PurchaseSupplierId!.Value
            })
            .Select(group => group.First())
            .ToList();
        if (documents.Count == 0) return [];
        if (documents.Any(x => !x.CustomerId.HasValue || x.CustomerId.Value == Guid.Empty))
            throw new PurchaseDraftException("CUSTOMER_REQUIRED", "采购预览缺少可确认的客户身份，不能构造单头。");

        var customerIds = documents.Select(x => x.CustomerId!.Value).Distinct().ToArray();
        var supplierIds = documents.Select(x => x.PurchaseSupplierId!.Value).Distinct().ToArray();
        var customers = (await customerRepository.GetByIdListAsync(customerIds, withUpdLock)).ToDictionary(x => x.Uid);
        var suppliers = (await supplierRepository.GetByIdListAsync(supplierIds, withUpdLock)).ToDictionary(x => x.Uid);
        var salesSourceIds = documents
            .Where(x => string.Equals(x.SourceType, DocumentTypeStaticsEnum.SalesOrder, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.SourceUid)
            .Distinct()
            .ToArray();
        var salesHeads = (await salesOrderHeadRepository.GetByIdListAsync(salesSourceIds, withUpdLock))
            .ToDictionary(x => x.Uid);
        if (salesSourceIds.Any(id => !salesHeads.ContainsKey(id)))
            throw new PurchaseDraftException("SOURCE_HEADER_NOT_FOUND", "销售来源单头已不存在，不能推断客户 PO 号或业务关联号。");

        var configs = new Dictionary<(Guid CustomerId, Guid SupplierId), SwiftExport.Contracts.DTOs.Customers.CustomerPoConfigDto?>();
        foreach (var key in documents
                     .Select(x => (CustomerId: x.CustomerId!.Value, SupplierId: x.PurchaseSupplierId!.Value))
                     .Distinct())
        {
            configs[key] = await customerPoConfigRepository.GetByPartiesAsync(
                key.CustomerId, key.SupplierId, cancellationToken, withUpdLock);
        }

        return documents.Select(document =>
        {
            var customerId = document.CustomerId!.Value;
            var supplierId = document.PurchaseSupplierId!.Value;
            if (!customers.TryGetValue(customerId, out var customer) || !suppliers.TryGetValue(supplierId, out var supplier))
                throw new PurchaseDraftException("MASTER_DATA_NOT_FOUND", "采购预览关联的客户或供应商主数据已不存在。");
            configs.TryGetValue((customerId, supplierId), out var config);
            salesHeads.TryGetValue(document.SourceUid, out var salesHead);
            var hasSalesSource = string.Equals(
                document.SourceType, DocumentTypeStaticsEnum.SalesOrder, StringComparison.OrdinalIgnoreCase);
            return new PurchaseDraftPreviewHeaderFact
            {
                SourceType = document.SourceType,
                SourceUid = document.SourceUid,
                SupplierId = supplierId,
                Header = BuildNewHeader(
                    customer,
                    supplier,
                    hasSalesSource ? salesHead : null,
                    config,
                    candidates
                        .Where(x => x.IsSelected && x.SourceUid == document.SourceUid
                            && string.Equals(x.SourceType, document.SourceType, StringComparison.OrdinalIgnoreCase)
                            && x.PurchaseSupplierId == supplierId)
                        .Select(x => x.PlannedDeliveryDate)
                        .Min())
            };
        }).ToList();
    }

    /// <summary>按已核验来源和默认配置建立新采购单头；没有销售来源时不猜测客户 PO 或内部关联号。</summary>
    internal static PurchaseDraftPreviewHeaderDto BuildNewHeader(
        Customer customer,
        Supplier supplier,
        SalesOrderHeader? salesHead,
        CustomerPoConfigDto? config,
        DateTime? expectedDate) => new()
    {
        OrderNo = string.Empty,
        CustomerPoNo = salesHead?.CustomerPoNo ?? string.Empty,
        BoNo = salesHead is null
            ? string.Empty
            : string.Concat(customer.DocRelateNo, salesHead.InvoiceNo, supplier.DocRelateNo),
        Currency = config?.Currency ?? string.Empty,
        DiscountRate = config?.DefaultDiscountRate,
        DiscountAmount = null,
        DiscountRemark = config?.DiscountRemark ?? string.Empty,
        OrderDate = null,
        ExpectedDate = expectedDate,
        InletHoseType = config?.InletHoseType ?? string.Empty,
        AeratorType = config?.AeratorType ?? string.Empty,
        PalletType = config?.PalletType ?? string.Empty,
        BoxType = config?.BoxType ?? string.Empty,
        LabelType = config?.LabelType ?? string.Empty,
        InstructionManualType = config?.IMType ?? string.Empty,
        CartonType = config?.CartonType ?? string.Empty,
        LogoType = config?.LogoType ?? string.Empty,
        SalesPerson = config?.SalesPerson ?? string.Empty,
        PurchasePerson = config?.Purchaser ?? string.Empty,
        Remark = config?.Note ?? string.Empty
    };

    /// <summary>把安全追加候选关联到既有采购头；既有单头始终优先于当前默认配置。</summary>
    private async Task AttachExistingHeadersAsync(
        IReadOnlyList<PurchaseDraftSafeAppendCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return;
        var heads = (await purchaseDraftRepository.GetPurchaseDraftsByIdsAsync(
            candidates.Select(x => x.PurchaseOrderId), cancellationToken)).ToDictionary(x => x.Uid);
        foreach (var candidate in candidates)
        {
            if (!heads.TryGetValue(candidate.PurchaseOrderId, out var head))
                throw new PurchaseDraftException("APPEND_TARGET_UNSAFE", "安全追加候选的采购单头已不存在。");
            candidate.Header = MapExistingHeader(head);
        }
    }

    /// <summary>把既有采购头映射为只读预览快照，不应用当前配置覆盖历史字段。</summary>
    private static PurchaseDraftPreviewHeaderDto MapExistingHeader(PurchaseOrderHead head) => new()
    {
        OrderNo = head.OrderNo,
        CustomerPoNo = head.CustomerPoNo ?? string.Empty,
        BoNo = head.BoNo ?? string.Empty,
        Currency = head.Currency,
        DiscountRate = head.DiscountRate,
        DiscountAmount = head.DiscountAmount,
        DiscountRemark = head.ZheKouBeiZhu ?? string.Empty,
        OrderDate = head.OrderDate,
        ExpectedDate = head.ExpectedDate,
        InletHoseType = head.InletrHoseType ?? string.Empty,
        AeratorType = head.AeratorType ?? string.Empty,
        PalletType = head.PalletType ?? string.Empty,
        BoxType = head.BoxType ?? string.Empty,
        LabelType = head.LabelType ?? string.Empty,
        InstructionManualType = head.IMType ?? string.Empty,
        CartonType = head.CartonType ?? string.Empty,
        LogoType = head.LogoType ?? string.Empty,
        SalesPerson = head.SoPerson ?? string.Empty,
        PurchasePerson = head.PoPerson ?? string.Empty,
        Remark = head.Remark ?? string.Empty
    };

    private async Task<Dictionary<(Guid? ProductId, Guid? SupplierId), SupplierProduct>> LoadPurchaseDataAsync(
        IEnumerable<(Guid? ProductId, Guid? SupplierId)> nullableKeys)
    {
        var keys = nullableKeys
            .Where(x => x.ProductId.HasValue && x.SupplierId.HasValue)
            .Select(x => (x.ProductId!.Value, x.SupplierId!.Value))
            .Distinct()
            .ToArray();
        var rows = await supplierProductRepository.GetPurchaseDataAsync(keys);
        var duplicates = rows.GroupBy(x => (x.SkuId, x.SupplierId)).Where(x => x.Count() > 1).ToArray();
        if (duplicates.Length > 0)
            throw new PurchaseDraftException("SUPPLIER_PRODUCT_DUPLICATE", "存在重复的活动供应商产品资料，不能确定采购价格。");
        return rows.ToDictionary(
            x => ((Guid?)x.SkuId, (Guid?)x.SupplierId),
            x => x);
    }

    /// <summary>映射候选行及实际供应商选择，显式空送达方不回退到计划建议。</summary>
    private static PurchaseDraftPreviewCandidate MapWorkspaceCandidate(
        MaterialDemandManagerItemDto row,
        PurchaseDraftPreviewItem? item,
        Guid commandItemId,
        bool selected,
        SupplierProduct? data,
        IReadOnlyDictionary<Guid, Supplier> suppliers)
    {
        var purchaseSupplierId = item?.SupplierId ?? row.SuggestedPurchaseSupplierId;
        // 工作台预览同样尊重命令项的显式空值；仅无命令项的一键入口沿用建议送达方。
        var deliverySupplierId = item is null
            ? row.SuggestedDeliverySupplierId
            : item.DeliveryTargetSupplierId;
        suppliers.TryGetValue(purchaseSupplierId ?? Guid.Empty, out var purchaseSupplier);
        suppliers.TryGetValue(deliverySupplierId ?? Guid.Empty, out var deliverySupplier);
        return new PurchaseDraftPreviewCandidate
        {
            CommandItemId = commandItemId,
            NetDemandId = row.Uid,
            NetDemandVersion = row.Version,
            SourceType = row.SourceType,
            SourceUid = row.SourceUid,
            SourceNo = row.SourceNo,
            SourceLineId = row.SourceLineId,
            SourceLineNumber = row.SourceLineNumber,
            CustomerId = row.CustomerId,
            CustomerCode = row.CustomerCode ?? string.Empty,
            CustomerName = row.CustomerName ?? string.Empty,
            IsSelected = selected,
            IsCustomerSupplied = row.IsCustomerSupplied,
            IsPlanningFactValid = row.NetQty > 0 && row.AssociatedPurchaseQty.HasValue
                && row.AssociatedPurchaseQty.Value <= row.NetQty && string.IsNullOrEmpty(row.ReviewReason),
            ExecutionType = ResolveExecutionType(row.SupplyType),
            RemainingQty = row.RemainingQty ?? 0m,
            RequestedPurchaseQty = item?.PurchaseQty,
            DemandRequiredDate = row.DemandRequiredDate,
            PlannedDeliveryDate = item is null ? row.DemandRequiredDate : item.PlannedDeliveryDate,
            ProductId = row.ProductId,
            Sku = row.Sku,
            LinkSku = row.LinkSku,
            CustomerSku = row.CustomerSku,
            Description = row.Description,
            Color = row.Color,
            Specification = row.Specification,
            ImagePath = row.ImagePath,
            MaterialType = row.MaterialType,
            Unit = EffectiveUnit(row.Unit),
            QuantityScale = 4,
            PurchaseSupplierId = purchaseSupplierId,
            PurchaseSupplierIsActive = purchaseSupplier?.IsActive == true,
            PurchaseSupplierCode = purchaseSupplier?.SupplierCode ?? string.Empty,
            PurchaseSupplierName = purchaseSupplier?.SupplierName ?? string.Empty,
            DeliveryTargetNetDemandId = row.DeliveryTargetNetDemandId,
            DeliveryTargetSupplierId = deliverySupplierId,
            DeliveryTargetIsValid = !row.IsCustomerSupplied
                || (row.DeliveryTargetNetDemandId.HasValue && deliverySupplier?.IsActive == true),
            DeliveryTargetSupplierCode = deliverySupplier?.SupplierCode ?? string.Empty,
            DeliveryTargetSupplierName = deliverySupplier?.SupplierName ?? string.Empty,
            HasSupplierProduct = data is not null,
            SupplierSku = data?.SupplierSku ?? string.Empty,
            UnitPrice = item is null ? data?.UnitPrice ?? 0m : item.UnitPrice,
            TaxPrice = item is null ? data?.TaxPrice ?? 0m : item.TaxPrice
        };
    }

    /// <summary>只传递物料单位快照；未知单位由预览阻断，不能默认 PCS。</summary>
    private static string EffectiveUnit(string? unit) => unit?.Trim() ?? string.Empty;

    private static PurchaseDraftPreviewBuildTarget MapTarget(PurchaseDraftTargetSelectionDto target)
        => new()
        {
            SourceType = target.SourceType,
            SourceUid = target.SourceUid,
            SupplierId = target.SupplierId,
            TargetMode = target.TargetMode,
            ExistingPurchaseOrderId = target.ExistingPurchaseOrderId,
            ExpectedPurchaseOrderVersion = target.ExpectedPurchaseOrderVersion
        };

    private static DemandExecutionTypeEnum ResolveExecutionType(string? supplyType)
        => string.Equals(supplyType, SupplyTypeEnums.生产, StringComparison.Ordinal)
            ? DemandExecutionTypeEnum.Production
            : string.Equals(supplyType, SupplyTypeEnums.外协, StringComparison.Ordinal)
                ? DemandExecutionTypeEnum.Outsourcing
                : DemandExecutionTypeEnum.Purchase;

    private static void ValidateLockedItems(
        PurchaseDraftEntryModeEnum entryMode,
        CreatePurchaseDraftSourceCommand source,
        IReadOnlyList<PurchaseDraftPreviewCandidate> candidates)
    {
        var candidateById = candidates.ToDictionary(x => x.NetDemandId);
        foreach (var item in source.Items)
        {
            if (!candidateById.TryGetValue(item.NetDemandId, out var candidate))
                throw new PurchaseDraftException("NET_DEMAND_NOT_FOUND", "物料需求不属于当前活动来源单。");
            if (candidate.NetDemandVersion != item.ExpectedVersion
                || candidate.RemainingQty != item.ExpectedRemainingQty)
                throw new PurchaseDraftException("PREVIEW_EXPIRED", "计划版本或剩余数量已变化，请重新预览。");
            if (item.PurchaseQty <= 0 || item.PurchaseQty > candidate.RemainingQty)
                throw new PurchaseDraftException("INVALID_PURCHASE_QUANTITY", "采购数量超出当前剩余量。");
        }

        if (entryMode == PurchaseDraftEntryModeEnum.SourceOneClick)
        {
            var required = candidates
                .Where(x => x.ExecutionType == DemandExecutionTypeEnum.Purchase && x.RemainingQty > 0)
                .Select(x => x.NetDemandId)
                .ToHashSet();
            if (!required.SetEquals(source.Items.Select(x => x.NetDemandId))
                || source.Items.Any(x => x.PurchaseQty != x.ExpectedRemainingQty))
                throw new PurchaseDraftException("ONE_CLICK_REQUIRES_ALL_REMAINING",
                    "来源一键转采购必须包含全部采购型剩余计划并使用全部剩余量。");
        }
    }

    /// <summary>校验入口模式、预览身份、目标与命令项格式，不修改业务事实。</summary>
    private static void ValidatePreviewRequest(PurchaseDraftPreviewRequest request)
    {
        if (request is null || request.ExecutionRequestId == Guid.Empty)
            throw new PurchaseDraftException("EXECUTION_REQUEST_REQUIRED", "执行请求 Uid 不能为空。");
        request.Items ??= new List<PurchaseDraftPreviewItem>();
        if (!Enum.IsDefined(request.EntryMode) || !Enum.IsDefined(request.LineMode))
            throw new PurchaseDraftException("INVALID_PREVIEW_MODE", "采购预览模式无效。");
        request.SourceType = request.SourceType?.Trim();
        request.Targets ??= new List<PurchaseDraftTargetSelectionDto>();
        foreach (var target in request.Targets)
        {
            target.SourceType = target.SourceType?.Trim();
            if (string.IsNullOrWhiteSpace(target.SourceType) || target.SourceUid == Guid.Empty
                || target.SupplierId == Guid.Empty || !Enum.IsDefined(target.TargetMode)
                || (target.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting
                    && (!target.ExistingPurchaseOrderId.HasValue
                        || target.ExistingPurchaseOrderId.Value == Guid.Empty
                        || !target.ExpectedPurchaseOrderVersion.HasValue
                        || target.ExpectedPurchaseOrderVersion.Value <= 0)))
                throw new PurchaseDraftException("INVALID_TARGET", "采购草稿目标无效。");
        }
        if (request.Targets.GroupBy(x => (x.SourceType?.ToUpperInvariant(), x.SourceUid, x.SupplierId)).Any(x => x.Count() > 1))
            throw new PurchaseDraftException("DUPLICATE_TARGET", "同一来源供应商存在重复采购草稿目标。");
        if (request.EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick)
        {
            if (string.IsNullOrWhiteSpace(request.SourceType)
                || !request.SourceUid.HasValue || request.SourceUid.Value == Guid.Empty)
                throw new PurchaseDraftException("SOURCE_REQUIRED", "来源一键预览必须指定来源单。");
            if (request.Targets.Any(x => x.TargetMode != PurchaseDraftTargetModeEnum.CreateNew))
                throw new PurchaseDraftException("ONE_CLICK_MUST_CREATE_NEW", "来源一键预览只能新建采购草稿。");
        }
        else if (request.Items is null || request.Items.Count == 0)
        {
            throw new PurchaseDraftException("ITEM_REQUIRED", "人工预览至少选择一条计划。");
        }
        ValidatePreviewItems(request.Items);
    }

    private static void ValidatePreviewItems(IReadOnlyList<PurchaseDraftPreviewItem> items)
    {
        if (items.Any(x => x.CommandItemId == Guid.Empty || x.NetDemandId == Guid.Empty
                           || x.ExpectedVersion <= 0 || x.ExpectedRemainingQty < 0)
            || items.GroupBy(x => x.CommandItemId).Any(x => x.Count() > 1)
            || items.GroupBy(x => x.NetDemandId).Any(x => x.Count() > 1))
            throw new PurchaseDraftException("INVALID_ITEM", "预览包含无效或重复的命令项/物料需求。");
    }

}
