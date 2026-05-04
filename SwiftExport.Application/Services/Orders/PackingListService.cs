using Dapper;
using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

public class PackingListService(
    IPackingListHeadRepository headRepository,
    IPackingListLineRepository lineRepository,
    IPurchaseReceiveHeadRepository receiveHeadRepository,
    IPurchaseReceiveLineRepository receiveLineRepository,
    IPurchaseOrderLineRepository purchaseOrderLineRepository,
    IRepository<SalesOrderHeader> salesOrderHeaderRepository,
    IIndependentDemandRepository salesDemandRepository,
    IRepository<Product> productRepository,
    IBomRepository bomRepository,
    IInventoryRepository inventoryRepository,
    IStockService stockService,
    IDocumentNoService documentNoService,
    ServiceBaseContext context)
    : BaseService<PackingListHead, PackingListDto>(headRepository, context), IPackingListService
{
    private const string VirtualWhseCode = "V_WH";
    private readonly IPackingListHeadRepository _headRepository = headRepository;
    private readonly IPackingListLineRepository _lineRepository = lineRepository;
    private readonly IPurchaseReceiveHeadRepository _receiveHeadRepository = receiveHeadRepository;
    private readonly IPurchaseReceiveLineRepository _receiveLineRepository = receiveLineRepository;
    private readonly IPurchaseOrderLineRepository _purchaseOrderLineRepository = purchaseOrderLineRepository;
    private readonly IRepository<SalesOrderHeader> _salesOrderHeaderRepository = salesOrderHeaderRepository;
    private readonly IIndependentDemandRepository _salesDemandRepository = salesDemandRepository;
    private readonly IRepository<Product> _productRepository = productRepository;
    private readonly IBomRepository _bomRepository = bomRepository;
    private readonly IInventoryRepository _inventoryRepository = inventoryRepository;
    private readonly IStockService _stockService = stockService;
    private readonly IDocumentNoService _documentNoService = documentNoService;

    public async Task<ServiceResult<PagedResultDto<PackingListDto>>> GetPagedListAsync(PackingListSearchDto search)
    {
        search ??= new PackingListSearchDto();
        var criteria = new QueryCriteria();

        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            criteria.AddOrGroup(
                ("PackingListNo", 查询操作符.模糊匹配, keyword),
                ("PoNo", 查询操作符.模糊匹配, keyword),
                ("SalesOrderNo", 查询操作符.模糊匹配, keyword),
                ("SupplierCode", 查询操作符.模糊匹配, keyword));
        }

        if (!string.IsNullOrWhiteSpace(search.PackingListNo))
            criteria.Add("PackingListNo", 查询操作符.模糊匹配, search.PackingListNo.Trim());
        if (!string.IsNullOrWhiteSpace(search.SupplierCode))
            criteria.Add("SupplierCode", 查询操作符.等于, search.SupplierCode.Trim());
        if (!string.IsNullOrWhiteSpace(search.PoNo))
            criteria.Add("PoNo", 查询操作符.模糊匹配, search.PoNo.Trim());
        if (!string.IsNullOrWhiteSpace(search.SalesOrderNo))
            criteria.Add("SalesOrderNo", 查询操作符.模糊匹配, search.SalesOrderNo.Trim());
        if (search.IsPosted.HasValue)
            criteria.Add("IsPosted", 查询操作符.等于, search.IsPosted.Value);

        var paged = await _headRepository.GetPagedListAsync(criteria, search.PageIndex <= 0 ? 1 : search.PageIndex, search.PageSize <= 0 ? 50 : search.PageSize, "Id DESC");
        var items = paged.Items.Adapt<List<PackingListDto>>();

        foreach (var dto in items)
        {
            var lines = await _lineRepository.GetByPackingListNoAsync(dto.PackingListNo);
            dto.LineCount = lines.Count;
            dto.TotalQty = lines.Sum(x => x.Qty);
        }

        return ServiceResult<PagedResultDto<PackingListDto>>.Ok(new PagedResultDto<PackingListDto>
        {
            Items = items,
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }

    public async Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(int id)
    {
        var head = await _headRepository.GetByIdAsync(id);
        if (head == null)
            return ServiceResult<PackingListDetailDto>.Fail("装箱单不存在");

        var lines = await _lineRepository.GetByPackingListNoAsync(head.PackingListNo);
        return ServiceResult<PackingListDetailDto>.Ok(new PackingListDetailDto
        {
            Header = BuildHeaderDto(head, lines),
            Lines = lines.Adapt<List<PackingListLineDto>>()
        });
    }

    public async Task<ServiceResult<int>> SaveDetailAsync(PackingListDetailDto request)
    {
        if (request?.Header == null)
            return ServiceResult<int>.Fail("装箱单头不能为空");

        var head = request.Header.Adapt<PackingListHead>();
        if (string.IsNullOrWhiteSpace(head.PackingListNo))
            head.PackingListNo = await CreatePackingListNoAsync();

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            PackingListHead persisted;
            if (head.Id > 0)
            {
                var existing = await _headRepository.GetByIdAsync(head.Id);
                if (existing == null)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<int>.Fail("装箱单不存在，无法更新");
                }

                persisted = existing;

                if (persisted.IsPosted)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<int>.Fail("装箱单已过账，不能修改");
                }

                request.Header.Adapt(persisted);
                persisted.PackingListNo = head.PackingListNo.Trim();
                persisted.UpdatedDate = DateTime.Now;
                await _headRepository.UpdateAsync(persisted);
            }
            else
            {
                persisted = head;
                persisted.PackingListNo = head.PackingListNo.Trim();
                persisted.PackingDate ??= DateTime.Now;
                persisted.Status = string.IsNullOrWhiteSpace(persisted.Status) ? "草稿" : persisted.Status;
                persisted.CreatedDate = DateTime.Now;
                persisted.IsDeleted = false;
                persisted.Id = await _headRepository.AddAsync(persisted);
            }

            var oldLines = await _lineRepository.GetByPackingListNoAsync(persisted.PackingListNo);
            if (oldLines.Count > 0)
                await _lineRepository.DeleteRangeAsync(oldLines.Select(x => x.Id), false);

            var newLines = (request.Lines ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku) && x.Qty > 0)
                .Select(x =>
                {
                    var line = x.Adapt<PackingListLine>();
                    line.Id = 0;
                    line.PackingListHeadId = persisted.Id;
                    line.PackingListNo = persisted.PackingListNo;
                    line.PoNo = string.IsNullOrWhiteSpace(line.PoNo) ? persisted.PoNo : line.PoNo;
                    line.PurchaseOrderNo = string.IsNullOrWhiteSpace(line.PurchaseOrderNo) ? persisted.PurchaseOrderNo : line.PurchaseOrderNo;
                    line.SalesOrderNo = string.IsNullOrWhiteSpace(line.SalesOrderNo) ? persisted.SalesOrderNo : line.SalesOrderNo;
                    line.CreatedDate = DateTime.Now;
                    line.IsDeleted = false;
                    return line;
                })
                .ToList();

            if (newLines.Count > 0)
                await _lineRepository.AddRangeAsync(newLines);

            _session.Transaction.Commit();
            return ServiceResult<int>.Ok(persisted.Id);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<int>.Fail("保存装箱单失败：" + ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    public async Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync(PackingListPostRequestDto request)
    {
        if (request == null || request.PackingListId <= 0)
            return ServiceResult<PackingListPostResultDto>.Fail("过账请求无效");

        var head = await _headRepository.GetByIdAsync(request.PackingListId);
        if (head == null)
            return ServiceResult<PackingListPostResultDto>.Fail("装箱单不存在");
        if (head.IsPosted)
            return ServiceResult<PackingListPostResultDto>.Fail("装箱单已过账");
        if (string.IsNullOrWhiteSpace(head.SupplierCode))
            return ServiceResult<PackingListPostResultDto>.Fail("装箱单缺少供应商");

        var lines = (await _lineRepository.GetByPackingListNoAsync(head.PackingListNo)).Where(x => x.Qty > 0).ToList();
        if (lines.Count == 0)
            return ServiceResult<PackingListPostResultDto>.Fail("装箱单明细不能为空");

        var shortageResult = await CheckComponentShortagesAsync(head, lines);
        if (shortageResult.Count > 0 && !request.AllowNegativeStock)
        {
            return ServiceResult<PackingListPostResultDto>.Ok(new PackingListPostResultDto
            {
                PackingListNo = head.PackingListNo,
                NeedNegativeStockConfirm = true,
                WarningMessage = "客供件库存不足以核销，是否允许负库存出库？选择否将停止过账并提醒补货。",
                Shortages = shortageResult
            }, "客供件库存不足，需要确认是否允许负库存出库");
        }

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var receiveNo = await CreateReceiveNoAsync(head.PackingDate);
            await CreateVirtualReceiveAsync(head, lines, receiveNo, request.Operator);
            await CreateSalesShipmentAsync(head, lines);
            await WriteOffComponentsAsync(head, lines, request.AllowNegativeStock);

            head.IsPosted = true;
            head.Status = "已过账";
            head.PostedDate = DateTime.Now;
            head.PostedBy = request.Operator;
            head.ReceiveNo = receiveNo;
            head.UpdatedDate = DateTime.Now;
            await _headRepository.UpdateAsync(head);

            _session.Transaction.Commit();

            return ServiceResult<PackingListPostResultDto>.Ok(new PackingListPostResultDto
            {
                PackingListNo = head.PackingListNo,
                ReceiveNo = receiveNo,
                NeedNegativeStockConfirm = false
            });
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<PackingListPostResultDto>.Fail("装箱单过账失败：" + ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private async Task CreateVirtualReceiveAsync(PackingListHead head, List<PackingListLine> lines, string receiveNo, string? handler)
    {
        await _receiveHeadRepository.AddAsync(new PurchaseReceiveHead
        {
            ReceiveNo = receiveNo,
            PoNo = head.PoNo,
            OrderNo = head.PurchaseOrderNo,
            SupplierCode = head.SupplierCode,
            ReceiveDate = head.PackingDate ?? DateTime.Now,
            Handler = handler,
            Remark = "Packing List 虚拟入库：" + head.PackingListNo,
            CreatedDate = DateTime.Now,
            IsDeleted = false
        });

        var receiveLines = lines.Select(x => new PurchaseReceiveLine
        {
            ReceiveNo = receiveNo,
            Sku = x.Sku.Trim(),
            Qty = x.Qty,
            Unit = x.Unit,
            PoLineId = x.PoLineId,
            SoLineId = x.SoLineId,
            CreatedDate = DateTime.Now,
            IsDeleted = false
        }).ToList();

        await _receiveLineRepository.AddRangeAsync(receiveLines);

        foreach (var poGroup in lines.Where(x => x.PoLineId.HasValue).GroupBy(x => x.PoLineId!.Value))
        {
            var poLine = await _purchaseOrderLineRepository.GetByIdAsync(poGroup.Key);
            if (poLine == null) continue;

            poLine.RecQty += poGroup.Sum(x => x.Qty);
            poLine.LastRecDate = head.PackingDate ?? DateTime.Now;
            poLine.ChangeTime = DateTime.Now;
            await _purchaseOrderLineRepository.UpdateAsync(poLine);
        }

        foreach (var group in lines.GroupBy(x => new { Sku = x.Sku.Trim(), x.Unit }))
        {
            await IncreaseInventoryAsync(VirtualWhseCode, head.SupplierCode.Trim(), group.Key.Sku, group.Sum(x => x.Qty), group.Key.Unit, "PackingListReceive", receiveNo);
        }
    }

    private async Task CreateSalesShipmentAsync(PackingListHead head, List<PackingListLine> lines)
    {
        foreach (var group in lines.GroupBy(x => x.Sku.Trim()))
        {
            var decrease = await _stockService.DecreaseStockAsync(VirtualWhseCode, head.SupplierCode.Trim(), group.Key, group.Sum(x => x.Qty), false, "PackingListShip", head.PackingListNo);
            if (!decrease.Success)
                throw new InvalidOperationException(decrease.Message);
        }

        foreach (var soGroup in lines.Where(x => x.SoLineId.HasValue).GroupBy(x => x.SoLineId!.Value))
        {
            if (soGroup.Key <= 0 || soGroup.Key > int.MaxValue) continue;

            var demands = await _salesDemandRepository.GetListBySourceTypeAndSourceIdAsync(OrderTypeEnums.销售订单, [(int)soGroup.Key]);
            if (demands == null || demands.Count == 0) continue;
            var demand = demands[0];

            demand.ShippedQty += soGroup.Sum(x => x.Qty);
            demand.LastShippedDate = DateTime.Now;
            demand.ChangeTime = DateTime.Now;

            // 中文注释：IndependentDemand状态闭环——已到货后发货满额转“已完成”，未到货时保持“采购中”。
            if (demand.ShippedQty >= demand.OriginalQty)
            {
                demand.StatusFlag = demand.FilledQty >= demand.OriginalQty
                    ? RequirementStatusEnums.已完成
                    : RequirementStatusEnums.采购中;
            }

            demand.UpdatedDate = DateTime.Now;
            await _salesDemandRepository.UpdateAsync(demand);
        }

        var touchedOrderNos = lines
            .Where(x => !string.IsNullOrWhiteSpace(x.SalesOrderNo))
            .Select(x => x.SalesOrderNo!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var orderNo in touchedOrderNos)
        {
            await UpdateSalesOrderStatusAsync(orderNo);
        }
    }

    private async Task WriteOffComponentsAsync(PackingListHead head, List<PackingListLine> lines, bool allowNegativeStock)
    {
        var requirements = await BuildComponentRequirementsAsync(lines);
        foreach (var (Sku, Qty) in requirements)
        {
            var result = await _stockService.DecreaseStockAsync(VirtualWhseCode, head.SupplierCode.Trim(), Sku, Qty, allowNegativeStock, "PackingListWriteOff", head.PackingListNo);
            if (!result.Success)
                throw new InvalidOperationException(result.Message);
        }
    }

    private async Task<List<PackingListStockShortageDto>> CheckComponentShortagesAsync(PackingListHead head, List<PackingListLine> lines)
    {
        var requirements = await BuildComponentRequirementsAsync(lines);
        var shortages = new List<PackingListStockShortageDto>();

        foreach (var (Sku, Qty) in requirements)
        {
            var stock = await _inventoryRepository.GetByWhseLocationSkuAsync(VirtualWhseCode, head.SupplierCode.Trim(), Sku);
            var onhand = stock?.OnhandQty ?? 0m;
            if (onhand < Qty)
            {
                shortages.Add(new PackingListStockShortageDto
                {
                    Sku = Sku,
                    SupplierCode = head.SupplierCode,
                    RequiredQty = Qty,
                    OnhandQty = onhand,
                    ShortageQty = Qty - onhand
                });
            }
        }

        return shortages;
    }

    private async Task<List<(string Sku, decimal Qty)>> BuildComponentRequirementsAsync(List<PackingListLine> lines)
    {
        var requirements = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var products = await _productRepository.GetListAsync("Sku IN @Skus", new { Skus = lines.Select(x => x.Sku.Trim()).Distinct().ToList() });
        var productMap = products
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (!productMap.TryGetValue(line.Sku.Trim(), out var product) || string.IsNullOrWhiteSpace(product.BomNo))
                continue;

            var bomItems = await _bomRepository.GetBomByBomNoAsync(product.BomNo.Trim());
            foreach (var bom in bomItems)
            {
                var component = await FindProductBySkuAsync(bom.SubSku);
                if (!ShouldDeductComponent(component, bom))
                    continue;

                var qtyPer = bom.Denominator == 0 ? bom.Numerator : (decimal)bom.Numerator / bom.Denominator;
                var requiredQty = line.Qty * qtyPer;
                if (requiredQty <= 0) continue;

                var sku = bom.SubSku.Trim();
                requirements[sku] = requirements.TryGetValue(sku, out var existing) ? existing + requiredQty : requiredQty;
            }
        }

        return [.. requirements.Select(x => (x.Key, x.Value))];
    }

    private async Task<Product?> FindProductBySkuAsync(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;
        var items = await _productRepository.GetListAsync(new { Sku = sku.Trim() });
        if (items == null || items.Count == 0) return null;
        return items[0];
    }

    private static bool ShouldDeductComponent(Product? component, Bom bom)
    {
        var text = string.Join(" ", new[]
        {
            component?.SupplyType,
            component?.MaterialType,
            component?.Note,
            bom.Note
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("客供", StringComparison.OrdinalIgnoreCase)
               || text.Contains("需扣减", StringComparison.OrdinalIgnoreCase)
               || text.Contains("扣减", StringComparison.OrdinalIgnoreCase)
               || text.Contains("零件", StringComparison.OrdinalIgnoreCase);
    }

    private async Task IncreaseInventoryAsync(string whseCode, string locationCode, string sku, decimal qty, string? unit, string bizType, string bizNo)
    {
        var stock = await _inventoryRepository.GetByWhseLocationSkuAsync(whseCode, locationCode, sku);
        if (stock == null)
        {
            await _inventoryRepository.AddAsync(new Inventory
            {
                WhseCode = whseCode,
                LocationCode = locationCode,
                Sku = sku,
                Unit = unit,
                OnhandQty = qty,
                CreatedDate = DateTime.Now,
                IsDeleted = false
            });
        }
        else
        {
            stock.OnhandQty += qty;
            stock.UpdatedDate = DateTime.Now;
            await _inventoryRepository.UpdateAsync(stock);
        }

        const string txSql = @"IF OBJECT_ID('dbo.InventoryTransaction','U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[InventoryTransaction]
    ([WhseCode],[LocationCode],[Sku],[Qty],[BizType],[BizNo],[CreatedDate])
    VALUES (@WhseCode,@LocationCode,@Sku,@Qty,@BizType,@BizNo,@CreatedDate)
END";

        await _session.Connection.ExecuteAsync(txSql, new
        {
            WhseCode = whseCode,
            LocationCode = locationCode,
            Sku = sku,
            Qty = qty,
            BizType = bizType,
            BizNo = bizNo,
            CreatedDate = DateTime.Now
        }, transaction: _session.Transaction);
    }

    private async Task UpdateSalesOrderStatusAsync(string orderNo)
    {
        var headers = await _salesOrderHeaderRepository.GetListAsync(new { OrderNo = orderNo });
        if (headers == null || headers.Count == 0) return;
        var header = headers[0];

        var demands = await _salesDemandRepository.GetListBySourceTypeAndSourceNoAsync(OrderTypeEnums.销售订单, orderNo);
        var totalQty = demands.Sum(x => x.OriginalQty);
        var shippedQty = demands.Sum(x => x.ShippedQty);

        header.TotalShippedQty = shippedQty;
        header.TotalUnshippedQty = Math.Max(totalQty - shippedQty, 0m);
        header.OrderStatus = shippedQty <= 0
            ? SalesOrderStatusEnums.未发货
            : shippedQty >= totalQty
                ? SalesOrderStatusEnums.全部发完
                : SalesOrderStatusEnums.部分发货;
        header.UpdatedDate = DateTime.Now;

        await _salesOrderHeaderRepository.UpdateAsync(header);
    }

    private async Task<string> CreateReceiveNoAsync(DateTime? businessDate)
    {
        var result = await _documentNoService.GetNextDocumentNoAsync(new DocumentNoRequestDto
        {
            DocType = DocumentTypeStaticEnums.OutboundOrder,
            BusinessDate = businessDate ?? DateTime.Now,
            SerialWidth = 4,
            Operator = "system"
        });

        return result.Success && result.Data != null && !string.IsNullOrWhiteSpace(result.Data.DocumentNo)
            ? "PLRV" + result.Data.DocumentNo
            : "PLRV" + DateTime.Now.ToString("yyyyMMddHHmmss");
    }

    private static Task<string> CreatePackingListNoAsync()
    {
        return Task.FromResult("PL" + DateTime.Now.ToString("yyyyMMddHHmmss"));
    }

    private static PackingListDto BuildHeaderDto(PackingListHead head, IReadOnlyList<PackingListLine> lines)
    {
        var dto = head.Adapt<PackingListDto>();
        dto.LineCount = lines.Count;
        dto.TotalQty = lines.Sum(x => x.Qty);
        return dto;
    }
}
