using Dapper;
using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Infrastructure.Data;
using System.Text;

using SwiftExport.Contracts.DTOs.PackingLists.Requests;
using SwiftExport.Contracts.DTOs.PackingLists.Responses;
namespace SwiftExport.Application.Services.Orders;

public partial class PackingListService(
    IPackingListHeadRepository headRepository,
    IPackingListLineRepository lineRepository,
    IReceiveHeadRepository receiveHeadRepository,
    IReceiveLineRepository receiveLineRepository,
    IPurchaseOrderLineRepository purchaseOrderLineRepository,
    ISalesOrderHeadRepository salesOrderHeaderRepository,
    IIndependentDemandRepository salesDemandRepository,
    IProductRepository productRepository,
    IBomRepository bomRepository,
    IInventoryRepository inventoryRepository,
    IStockService stockService,
    IDocumentNoService documentNoService,
    ServiceBaseContext context) : IPackingListService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<PackingListService> _logger = context.LoggerFactory.CreateLogger<PackingListService>();
    private const string VirtualWhseCode = "V_WH";
    private readonly IPackingListHeadRepository _headRepository = headRepository;
    private readonly IPackingListLineRepository _lineRepository = lineRepository;
    private readonly IReceiveHeadRepository _receiveHeadRepository = receiveHeadRepository;
    private readonly IReceiveLineRepository _receiveLineRepository = receiveLineRepository;
    private readonly IPurchaseOrderLineRepository _purchaseOrderLineRepository = purchaseOrderLineRepository;
    private readonly ISalesOrderHeadRepository _salesOrderHeaderRepository = salesOrderHeaderRepository;
    private readonly IIndependentDemandRepository _salesDemandRepository = salesDemandRepository;
    private readonly IProductRepository _productRepository = productRepository;
    private readonly IBomRepository _bomRepository = bomRepository;
    private readonly IInventoryRepository _inventoryRepository = inventoryRepository;
    private readonly IStockService _stockService = stockService;
    private readonly IDocumentNoService _documentNoService = documentNoService;

    public async Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetHeadPagedListAsync(PackingListSearchDto search)
    {
        search ??= new PackingListSearchDto();
        var criteria = new QueryCriteria();

        if (search.BatchKeywords != null && search.BatchKeywords.Count > 0)
        {
            foreach (var keyword in search.BatchKeywords)
            {
                var trimmedKeyword = keyword.Trim();
                criteria.AddOrGroup(
                    ("Sku", 查询操作符.模糊匹配, trimmedKeyword),
                    ("DescriptionEn", 查询操作符.模糊匹配, trimmedKeyword),
                    ("SalesOrderNo", 查询操作符.模糊匹配, trimmedKeyword),
                    ("SupplierCode", 查询操作符.模糊匹配, trimmedKeyword));
            }
        }

        if (!string.IsNullOrWhiteSpace(search.PackingListNo))
            criteria.Add("PackingListNo", 查询操作符.模糊匹配, search.PackingListNo.Trim());
        if (!string.IsNullOrWhiteSpace(search.SalesOrderNo))
            criteria.Add("SalesOrderNo", 查询操作符.模糊匹配, search.SalesOrderNo.Trim());
        if (search.IsPosted.HasValue)
            criteria.Add("IsPosted", 查询操作符.等于, search.IsPosted.Value);

        var paged = await _headRepository.GetPagedListAsync(criteria, search.PageIndex <= 0 ? 1 : search.PageIndex, search.PageSize <= 0 ? 50 : search.PageSize, "Id DESC");
        var items = paged.Items.Adapt<List<PackingListHeadDto>>();

        return ServiceResult<PagedResultDto<PackingListHeadDto>>.Ok(new PagedResultDto<PackingListHeadDto>
        {
            Items = items,
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }

    public async Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(Guid headid)
    {
       
        if (headid == Guid.Empty)
            return ServiceResult<PackingListDetailDto>.Fail("装箱单ID无效");
        var head = await _headRepository.GetDetailByHeadIdAsync(headid);
        if (head == null)
            return ServiceResult<PackingListDetailDto>.Fail("装箱单不存在");
        return ServiceResult<PackingListDetailDto>.Ok(head);

    }

    public async Task<ServiceResult<Guid>> SaveDetailAsync(PackingListDetailDto request)
    {
        if (request?.Header == null)
            return ServiceResult<Guid>.Fail("装箱单头不能为空");

        var head = request.Header.Adapt<PackingListHead>();
        if (string.IsNullOrWhiteSpace(head.PackingListNo))
            head.PackingListNo = await CreatePackingListNoAsync();

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            PackingListHead persisted;
            if (head.Uid != Guid.Empty)
            {
                var existing = await _headRepository.GetByIdAsync(head.Uid);
                if (existing == null)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<Guid>.Fail("装箱单不存在，无法更新");
                }

                persisted = existing;

                if (persisted.IsPosted)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<Guid>.Fail("装箱单已过账，不能修改");
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
                persisted.IsDeleted = false;
                persisted.Uid = await _headRepository.AddAsync(persisted);
            }

            var oldLines = await _lineRepository.GetByPackingListNoAsync(persisted.PackingListNo);
            if (oldLines.Count > 0)
                await _lineRepository.DeleteRangeAsync(oldLines.Select(x => x.Uid), false);

            var newLines = (request.Lines ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku) && x.Qty > 0)
                .Select(x =>
                {
                    var line = x.Adapt<PackingListLine>();
                    line.Uid = Guid.Empty;
                    line.PackingListHeadId = persisted.Uid;
                    line.PackingListNo = persisted.PackingListNo;
                    line.IsDeleted = false;
                    return line;
                })
                .ToList();

            if (newLines.Count > 0)
                await _lineRepository.AddRangeAsync(newLines);

            _session.Transaction.Commit();
            return ServiceResult<Guid>.Ok(persisted.Uid);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<Guid>.Fail("保存装箱单失败：" + ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    public async Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync(PackingListPostRequestDto request)
    {
        if (request == null || request.PackingListId == Guid.Empty)
            return ServiceResult<PackingListPostResultDto>.Fail("过账请求无效");

        var head = await _headRepository.GetByIdAsync(request.PackingListId);
        if (head == null)
            return ServiceResult<PackingListPostResultDto>.Fail("装箱单不存在");
        if (head.IsPosted)
            return ServiceResult<PackingListPostResultDto>.Fail("装箱单已过账");

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
            head.PostedDate = DateTime.Now;
            head.PostedBy = request.Operator;
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
        
    }

    private async Task CreateSalesShipmentAsync(PackingListHead head, List<PackingListLine> lines)
    {
       
    }

    private async Task WriteOffComponentsAsync(PackingListHead head, List<PackingListLine> lines, bool allowNegativeStock)
    {

    }

    private async Task<List<PackingListStockShortageDto>> CheckComponentShortagesAsync(PackingListHead head, List<PackingListLine> lines)
    {
        var requirements = await BuildComponentRequirementsAsync(lines);
        var shortages = new List<PackingListStockShortageDto>();

        foreach (var (Sku, Qty) in requirements)
        {

            Inventory stock = new Inventory();
            var onhand = stock?.OnhandQty ?? 0m;
            if (onhand < Qty)
            {
                shortages.Add(new PackingListStockShortageDto
                {
                    Sku = Sku,
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

        var demands = await _salesDemandRepository.GetListBySourceTypeAndSourceNoAsync(OrderTypesEnum.销售订单, orderNo);
        var totalQty = demands.Sum(x => x.OriginalQty);
        var shippedQty = demands.Sum(x => x.ShippedQty);

        //header.OrderStatus = shippedQty <= 0
        //    ? SalesOrderStatusEnum.草稿
        //    : shippedQty >= totalQty
        //        ? SalesOrderStatusEnum.全部发完
        //        : SalesOrderStatusEnum.部分发货;
        header.UpdatedDate = DateTime.Now;

        await _salesOrderHeaderRepository.UpdateAsync(header);
    }

    private async Task<string> CreateReceiveNoAsync(DateTime? businessDate)
    {
        var result = await _documentNoService.GetNextDocumentNoAsync(new DocumentNoRequestDto
        {
            DocType = DocumentTypeStaticsEnum.OutboundOrder,
            BusinessDate = businessDate ?? DateTime.Now,
            SerialWidth = 4,
            Operator = "system"
        });

        return result.Success && result.Data != null && !string.IsNullOrWhiteSpace(result.Data.DocumentNo)
            ? "OB" + result.Data.DocumentNo
            : "OB" + DateTime.Now.ToString("yyyyMMddHHmmss");
    }

    private static Task<string> CreatePackingListNoAsync()
    {
        return Task.FromResult("OB" + DateTime.Now.ToString("yyyyMMddHHmmss"));
    }



    #region 显式基础读写

    public async Task<ServiceResult<PackingListHeadDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _headRepository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(PackingListHead));
                return ServiceResult<PackingListHeadDto>.Fail("未找到数据");
            }

            return ServiceResult<PackingListHeadDto>.Ok(entity.Adapt<PackingListHeadDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<PackingListHeadDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<PackingListHeadDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _headRepository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<PackingListHeadDto>>.Ok(entities.Adapt<IReadOnlyList<PackingListHeadDto>>())
                : ServiceResult<IReadOnlyList<PackingListHeadDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<PackingListHeadDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_headRepository is not IPagedCriteriaRepository<PackingListHead> pagedRepository)
                return ServiceResult<PagedResultDto<PackingListHeadDto>>.Fail($"{nameof(PackingListHead)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<PackingListHeadDto>>.Ok(new PagedResultDto<PackingListHeadDto>
            {
                Items = paged.Items.Adapt<List<PackingListHeadDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<PackingListHeadDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(PackingListHeadDto dto)
    {
        try
        {
            var entity = dto.Adapt<PackingListHead>();
            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;
            var uid = await _headRepository.AddAsync(entity);
            return ServiceResult<Guid>.Ok(uid);
        }
        catch (Exception ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> UpdateAsync(PackingListHeadDto dto)
    {
        try
        {
            var existing = await _headRepository.GetByIdAsync(dto.Uid);
            if (existing == null) return ServiceResult<int>.Fail("数据不存在");

            dto.Adapt(existing);
            await _headRepository.UpdateAsync(existing);
            return ServiceResult<int>.Ok(1);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> DeleteAsync(Guid uid)
    {
        try
        {
            return ServiceResult<int>.Ok(await _headRepository.DeleteAsync(uid));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PackingListBatchSaveResponse>> BatchSaveAsync(PackingListBatchSaveRequest batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new PackingListBatchSaveResponse();

        var createList = batchReq.AddList?.Adapt<List<PackingListHead>>();
        var updateList = batchReq.UpdateList?.Adapt<List<PackingListHead>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (updateList is { Count: > 0 })
            {
                var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

                var needUpdateUids = updateList.Select(x => x.Uid).ToList();
                var existingUids = await _headRepository.GetExistingIdListAsync(needUpdateUids);
                var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
                if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<PackingListBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

            if (_headRepository is not IBatchWriteRepository<PackingListHead> batchRepository)
                return ServiceResult<PackingListBatchSaveResponse>.Fail($"{nameof(PackingListHead)} 仓储未实现批量保存");

            if (createList is { Count: > 0 })
            {
                result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
            }

            if (updateList is { Count: > 0 })
            {
                await batchRepository.UpdateRangeAsync(updateList);
                result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
            }

            if (deleteIds is { Count: > 0 })
            {
                await batchRepository.DeleteRangeSoftAsync(deleteIds);
                result.DeleteIdList = deleteIds;
            }

            _session.Transaction.Commit();
            return ServiceResult<PackingListBatchSaveResponse>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<PackingListBatchSaveResponse>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    #endregion

}
