using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单拆解中间表应用服务。
/// </summary>
public class DependentDemandService(
    IDependentDemandRepository repository,
    IBomService bomService,
    IPurchaseOrderRepository purchaseOrderRepository,
    IPurchaseOrderLineRepository purchaseOrderItemRepository,
    IRepository<SalesOrderHeader> salesOrderHeaderRepository,
    IProductRepository productRepository,
    ISupplierRepository supplierRepository,
    IDocumentNoService documentNoService,
    ServiceBaseContext context)
    : BaseService<DependentDemand, DependentDemandDto>(repository, context), IDependentDemandService
{
    /// <summary>
    /// 中文注释：需求池转采购行的专用 Mapster 配置。
    /// 仅维护共性字段映射（如 SupplyType），其余依赖产品档案的字段在转单时补齐。
    /// </summary>
    private static readonly TypeAdapterConfig DependentDemandToPurchaseOrderLineMapConfig = BuildDependentDemandToPurchaseOrderLineMapConfig();

    private readonly IDependentDemandRepository _rpRepository = repository;
    private readonly IBomService _bomService = bomService;
    private readonly IPurchaseOrderRepository _purchaseOrderRepository = purchaseOrderRepository;
    private readonly IPurchaseOrderLineRepository _purchaseOrderItemRepository = purchaseOrderItemRepository;
    private readonly IRepository<SalesOrderHeader> _salesOrderHeaderRepository = salesOrderHeaderRepository;
    private readonly IProductRepository _productRepository = productRepository;
    private readonly ISupplierRepository _supplierRepository = supplierRepository;
    private readonly IDocumentNoService _documentNoService = documentNoService;

    /// <summary>
    /// 批量保存拆解结果到依赖需求池。
    /// </summary>
    public async Task<ServiceResult<int>> SaveToPoolAsync(List<DependentDemand> demands)
    {
        if (demands == null || demands.Count == 0)
            return ServiceResult<int>.Ok(0);

        var affectedIds = await _rpRepository.AddRangeAsync(demands);
        return ServiceResult<int>.Ok(affectedIds?.Count ?? 0);
    }

    /// <summary>
    /// 中文注释：构建需求池转采购行映射配置。
    /// </summary>
    private static TypeAdapterConfig BuildDependentDemandToPurchaseOrderLineMapConfig()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<DependentDemand, PurchaseOrderLine>()
            .Map(dest => dest.Sku, src => src.Sku)
            .Map(dest => dest.QTY, src => src.NetQty)
            .Map(dest => dest.SoLineId, src => src.SourceLineId)
            .Map(dest => dest.SupplyType, src => src.SupplyType)
            .Map(dest => dest.FollowExpectedDate, src => src.RequiredDate)
            .Map(dest => dest.SubNote, src => src.Remark);
        return config;
    }

    /// <summary>
    /// 中文注释：一键将需求池中所有待采购需求转为采购单。
    /// 规则：仅处理 StatusFlag=待处理 且 SupplyType=采购 的行。
    /// </summary>
    public async Task<List<string>> ConvertAllPendingToPurchaseOrdersAsync()
    {
        return await ConvertToPurchaseOrdersAsync(new DependentDemandConvertToPurchaseRequestDto
        {
            GroupMode = DependentDemandConvertGroupModes.BySourceNo
        });
    }

    /// <summary>
    /// 中文注释：更新需求池行手工供应商信息（方案A必需步骤）。
    /// </summary>
    public async Task<ServiceResult<bool>> UpdateSupplierAsync(int id, string supplierCode, string? supplierName)
    {
        if (id <= 0)
            return ServiceResult<bool>.Fail("需求池ID无效");

        if (string.IsNullOrWhiteSpace(supplierCode))
            return ServiceResult<bool>.Fail("供应商编码不能为空");

        var entity = await _rpRepository.GetByIdAsync(id);
        if (entity == null)
            return ServiceResult<bool>.Fail("需求池行不存在");

        entity.SupplierCode = supplierCode.Trim();
        entity.SupplierName = (supplierName ?? string.Empty).Trim();
        entity.UpdatedDate = DateTime.Now;
        entity.ChangeTime = DateTime.Now;

        await _rpRepository.UpdateAsync(entity);
        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>
    /// 分页查询拆解需求池列表。
    /// </summary>
    /// <param name="search">查询条件</param>
    /// <returns>分页结果</returns>
    public async Task<ServiceResult<PagedResultDto<DependentDemandDto>>> GetPagedListAsync(DependentDemandSearchDto search)
    {
        search ??= new DependentDemandSearchDto();

        var criteria = BuildCriteria(search);
        int pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
        int pageSize = search.PageSize > 0 ? search.PageSize : 50;

        var paged = await _rpRepository.GetPagedListAsync(criteria, pageIndex, pageSize, "Id DESC");

        return ServiceResult<PagedResultDto<DependentDemandDto>>.Ok(new PagedResultDto<DependentDemandDto>
        {
            Items = paged.Items.Adapt<List<DependentDemandDto>>(),
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }


    /// <summary>
    /// 构建分页查询条件。
    /// </summary>
    private static QueryCriteria BuildCriteria(DependentDemandSearchDto search)
    {
        var criteria = new QueryCriteria();

        if (!string.IsNullOrWhiteSpace(search.SourceNo))
            criteria.Add("SourceNo", 查询操作符.模糊匹配, search.SourceNo.Trim());

        if (!string.IsNullOrWhiteSpace(search.SourceType))
            criteria.Add("SourceType", 查询操作符.等于, search.SourceType.Trim());

        if (!string.IsNullOrWhiteSpace(search.Sku))
            criteria.Add("Sku", 查询操作符.模糊匹配, search.Sku.Trim());

        if (!string.IsNullOrWhiteSpace(search.SupplyType))
            criteria.Add("SupplyType", 查询操作符.等于, search.SupplyType.Trim());

        if (search.BomLevel.HasValue)
        {
            // 中文注释：层级过滤支持“成品/子件”工作台筛选。
            if (search.BomLevel.Value == 1)
                criteria.Add("BomLevel", 查询操作符.大于等于, 1);
            else
                criteria.Add("BomLevel", 查询操作符.等于, search.BomLevel.Value);
        }

        if (!string.IsNullOrWhiteSpace(search.StatusFlag))
            criteria.Add("StatusFlag", 查询操作符.等于, search.StatusFlag.Trim());

        if (search.IsKitted.HasValue)
            criteria.Add("IsKitted", 查询操作符.等于, search.IsKitted.Value);

        if (!string.IsNullOrWhiteSpace(search.SupplierKeyword))
        {
            var supplierKeyword = search.SupplierKeyword.Trim();
            criteria.AddOrGroup(
                ("SupplierCode", 查询操作符.模糊匹配, supplierKeyword),
                ("SupplierName", 查询操作符.模糊匹配, supplierKeyword));
        }

        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            criteria.AddOrGroup(
                ("SourceNo", 查询操作符.模糊匹配, keyword),
                ("Sku", 查询操作符.模糊匹配, keyword),
                ("TargetNo", 查询操作符.模糊匹配, keyword));
        }

        return criteria;
    }


    /// <summary>
    /// 兼容旧入口：拆解并写入依赖需求池（委托至BomService计算 + SaveToPool持久化）。
    /// </summary>
    public async Task<ServiceResult<List<DependentDemand>>> ExecuteDecomposeToPoolAsync(DecomposeRequestDto source)
    {
        if (source == null)
            return ServiceResult<List<DependentDemand>>.Fail("拆解源不能为空");

        var list = await ExecuteDecomposeToPoolAsync([source]);
        return list;
    }

    /// <summary>
    /// 兼容旧入口：批量拆解并写入依赖需求池。
    /// </summary>
    public async Task<ServiceResult<List<DependentDemand>>> ExecuteDecomposeToPoolAsync(List<DecomposeRequestDto> sourceList)
    {
        if (sourceList == null || sourceList.Count == 0)
            return ServiceResult<List<DependentDemand>>.Fail("拆解源列表不能为空");

        var result = new List<DependentDemand>();
        foreach (var source in sourceList)
        {
            var computed = await _bomService.CalculateDecomposeAsync(source);
            if (computed.Count == 0)
                continue;

            var saveRst = await SaveToPoolAsync(computed);
            if (!saveRst.Success)
                return ServiceResult<List<DependentDemand>>.Fail(saveRst.Message);

            result.AddRange(computed);
        }

        return ServiceResult<List<DependentDemand>>.Ok(result);
    }

    /// <summary>
    /// 根据来源单据判断是否允许回退审批。
    /// 规则：需求池中存在执行态（StatusFlag != 待处理）则不允许回退。
    /// </summary>
    public async Task<ServiceResult<bool>> CanRollbackBySourceAsync(string sourceType, string sourceNo)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
            return ServiceResult<bool>.Fail("来源类型和来源单号不能为空");

        var pools = await _rpRepository.GetBySourceAsync(sourceType, sourceNo);
        if (pools == null || pools.Count == 0)
            return ServiceResult<bool>.Ok(true);

        var hasExecuted = pools.Any(x => x.StatusFlag != RequirementStatusEnums.待处理);
        if (hasExecuted)
            return ServiceResult<bool>.Fail("订单中存在已执行记录，不允许回退审批");

        // 中文注释：若已挂接目标执行单（计划单/采购单），即便状态未推进，也不允许回退。
        var hasTargetLinked = pools.Any(x => !string.IsNullOrWhiteSpace(x.TargetNo) || x.TargetLineId.HasValue);
        if (hasTargetLinked)
            return ServiceResult<bool>.Fail("订单中存在已关联执行单，不允许回退审批");

        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>
    /// 按来源删除需求池记录（用于审批回退）。
    /// </summary>
    public async Task<ServiceResult<int>> RemoveBySourceAsync(string sourceType, string sourceNo)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
            return ServiceResult<int>.Fail("来源类型和来源单号不能为空");

        var deleteCount = await _rpRepository.DeleteBySourceAsync(sourceType, sourceNo);
        return ServiceResult<int>.Ok(deleteCount);
    }

    /// <summary>
    /// 将某个来源单据下的待采购需求按供应商分组转为采购单。
    /// 逻辑：
    /// 1) 仅处理 SupplyType=Purchase/直接采购 且 StatusFlag=待处理 的行；
    /// 2) 按 VendorCode 分组，一组生成一张采购单；
    /// 3) 回写 DependentDemand 的 TargetNo 与 StatusFlag=采购中。
    /// </summary>
    public async Task<List<string>> ConvertSourceToPurchaseOrdersAsync(string sourceType, string sourceNo)
    {
        return await ConvertToPurchaseOrdersAsync(new DependentDemandConvertToPurchaseRequestDto
        {
            GroupMode = DependentDemandConvertGroupModes.BySourceNo,
            SourceType = sourceType,
            SourceNos = [sourceNo]
        });
    }

    /// <summary>
    /// 通用转采购入口：支持按来源单号、按供应商、按选中需求池ID集合分组。
    /// </summary>
    public async Task<List<string>> ConvertToPurchaseOrdersAsync(DependentDemandConvertToPurchaseRequestDto request)
    {
        request ??= new DependentDemandConvertToPurchaseRequestDto();

        var pools = await LoadPendingPurchasePoolsAsync(request);
        if (pools.Count == 0)
            return [];

        // 中文注释：先构建产品索引，用于价格回填；供应商优先取需求池行手工指定值，未指定时再回退到产品默认供应商。
        var products = await _productRepository.GetBySkuListAsync([.. pools
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .Select(x => x.Sku)
            .Distinct(StringComparer.OrdinalIgnoreCase)]);

        var productBySku = products
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .ToDictionary(x => x.Sku!, x => x, StringComparer.OrdinalIgnoreCase);

        var defaultSupplierBySku = products
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku) && !string.IsNullOrWhiteSpace(x.SupplierCode))
            .ToDictionary(x => x.Sku!, x => x.SupplierCode!, StringComparer.OrdinalIgnoreCase);

        var invalidRows = pools
            .Where(x => string.IsNullOrWhiteSpace(ResolveSupplierCode(x, defaultSupplierBySku)))
            .Select(x => $"Id={x.Id},SourceNo={x.SourceNo},LineId={x.SourceLineId},Sku={(string.IsNullOrWhiteSpace(x.Sku) ? "(空SKU)" : x.Sku)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalidRows.Count > 0)
            throw new Exception("以下需求池行未指定供应商，请先手工指定后再转单：" + string.Join("; ", invalidRows));

        var supplierCodes = pools
            .Select(x => ResolveSupplierCode(x, defaultSupplierBySku))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var supplierList = await _supplierRepository.GetBySupplierCodeListAsync(supplierCodes);
        var supplierNameByCode = supplierList
            .Where(x => !string.IsNullOrWhiteSpace(x.SupplierCode))
            .ToDictionary(
                x => x.SupplierCode!,
                x => string.IsNullOrWhiteSpace(x.SupplierName) ? x.SupplierCode! : x.SupplierName,
                StringComparer.OrdinalIgnoreCase);

        var groups = request.GroupMode switch
        {
            DependentDemandConvertGroupModes.BySupplierCode => [.. pools.GroupBy(x => ResolveSupplierCode(x, defaultSupplierBySku), StringComparer.OrdinalIgnoreCase)],

            DependentDemandConvertGroupModes.ByDependentDemandIds => [.. pools.GroupBy(x => (x.SourceNo ?? string.Empty).Trim() + "||" + ResolveSupplierCode(x, defaultSupplierBySku), StringComparer.OrdinalIgnoreCase)],

            _ => pools
                .GroupBy(x => (x.SourceNo ?? string.Empty).Trim() + "||" + ResolveSupplierCode(x, defaultSupplierBySku), StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var createdOrderNos = new List<string>();
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            foreach (var group in groups)
            {
                var groupList = group.ToList();
                if (groupList.Count == 0) continue;

                var first = groupList[0];
                var orderNo = await BuildOrderNoByDocumentServiceAsync();

                var supplierCode = ResolveSupplierCode(first, defaultSupplierBySku);
                var supplierName = supplierNameByCode.TryGetValue(supplierCode, out var name) ? name : supplierCode;
                var supplier = supplierList.FirstOrDefault(x => string.Equals(x.SupplierCode, supplierCode, StringComparison.OrdinalIgnoreCase));

                var sourceNoList = groupList
                    .Where(x => !string.IsNullOrWhiteSpace(x.SourceNo))
                    .Select(x => x.SourceNo.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var expectedDate = groupList
                    .Where(x => x.RequiredDate.HasValue)
                    .Select(x => x.RequiredDate!.Value)
                    .DefaultIfEmpty(DateTime.Now)
                    .Min();

                if (expectedDate <= DateTime.MinValue)
                {
                    var leadTime = supplier?.DefaultLeadTime ?? 0;
                    expectedDate = DateTime.Now.AddDays(leadTime > 0 ? leadTime : 0);
                }

                var po = new PurchaseOrderHead
                {
                    PoNo = orderNo,
                    SalesOrderNo = sourceNoList.Count == 1 ? sourceNoList[0] : string.Empty,
                    OrderNo = orderNo,
                    OrderDate = DateTime.Now,
                    ExpectedDate = expectedDate,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    Currency = supplier?.Currency,
                    Remark = "来源单据：" + string.Join(",", sourceNoList),
                    LineCount = groupList.Count,
                    TotalQty = groupList.Sum(x => x.NetQty),
                    TotalAmountLocalCurrency = groupList.Sum(x => ResolveUnitPriceBySku(x.Sku, productBySku) * x.NetQty),
                    IsDeleted = false,
                    CreatedDate = DateTime.Now,
                    Status = null,
                    Even = null
                };
                await _purchaseOrderRepository.AddAsync(po);

                var itemList = groupList.Select(x =>
                {
                    productBySku.TryGetValue(x.Sku, out var product);
                    var unitPrice = ResolveUnitPriceBySku(x.Sku, productBySku);
                    var item = x.Adapt<PurchaseOrderLine>(DependentDemandToPurchaseOrderLineMapConfig);

                    item.OrderNo = orderNo;
                    item.UnitPrice = unitPrice;
                    item.UnitAmount = unitPrice * x.NetQty;
                    item.MaterialType = product?.MaterialType;
                    item.UnitCBM = product?.UnitCBM;
                    item.UnitWeight = product?.UnitWeight;
                    item.ReadyQty = 0;
                    item.RecQty = 0;
                    item.IsDeleted = false;
                    item.CreatedDate = DateTime.Now;
                    item.Status = null;
                    item.Even = null;

                    return item;
                }).ToList();

                if (itemList.Count > 0)
                    await _purchaseOrderItemRepository.AddRangeAsync(itemList);

                foreach (var rp in groupList)
                {
                    rp.TargetNo = orderNo;
                    rp.StatusFlag = RequirementStatusEnums.采购中;
                    rp.ChangeTime = DateTime.Now;
                }
                await _rpRepository.UpdateRangeAsync(groupList);

                createdOrderNos.Add(orderNo);
            }

            _session.Transaction.Commit();
            return createdOrderNos;
        }
        catch
        {
            _session.Transaction.Rollback();
            throw;
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>
    /// 加载待处理采购需求池行，并按请求进行范围过滤。
    /// </summary>
    private async Task<List<DependentDemand>> LoadPendingPurchasePoolsAsync(DependentDemandConvertToPurchaseRequestDto request)
    {
        List<DependentDemand> pools;

        if (request.DependentDemandIds?.Count > 0)
        {
            var ids = request.DependentDemandIds.Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0) return [];
            pools = [.. await _rpRepository.GetByIdListAsync(ids)];
        }
        else
        {
            var criteria = new QueryCriteria();
            criteria.Add("StatusFlag", 查询操作符.等于, RequirementStatusEnums.待处理);
            pools = [.. await _rpRepository.GetListAsync(criteria)];
        }

        pools = [.. pools
            .Where(x => x != null && x.StatusFlag == RequirementStatusEnums.待处理)
            .Where(x => IsPurchaseSupplyType(x.SupplyType))];

        if (!string.IsNullOrWhiteSpace(request.SourceType))
        {
            var sourceType = request.SourceType.Trim();
            pools = [.. pools.Where(x => string.Equals(x.SourceType, sourceType, StringComparison.OrdinalIgnoreCase))];
        }

        if (request.SourceNos?.Count > 0)
        {
            var sourceNoSet = new HashSet<string>(request.SourceNos
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);

            pools = [.. pools.Where(x => !string.IsNullOrWhiteSpace(x.SourceNo) && sourceNoSet.Contains(x.SourceNo.Trim()))];
        }

        if (!string.IsNullOrWhiteSpace(request.SupplierCode))
        {
            var supplierCode = request.SupplierCode.Trim();
            pools = [.. pools.Where(x => string.Equals(x.SupplierCode, supplierCode, StringComparison.OrdinalIgnoreCase))];
        }

        return pools;
    }

    /// <summary>
    /// 判断是否采购类型（兼容历史英文值与新枚举值）。
    /// </summary>
    private static bool IsPurchaseSupplyType(string? supplyType)
    {
        if (string.IsNullOrWhiteSpace(supplyType))
            return false;

        return string.Equals(supplyType, SupplyTypeEnums.采购, StringComparison.OrdinalIgnoreCase)
               || string.Equals(supplyType, SupplyTypeEnums.生产, StringComparison.OrdinalIgnoreCase)
               || string.Equals(supplyType, SupplyTypeEnums.外协, StringComparison.OrdinalIgnoreCase)
               || string.Equals(supplyType, "Purchase", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 通过单号服务生成系统内码（OrderNo）。
    /// </summary>
    private async Task<string> BuildOrderNoByDocumentServiceAsync()
    {
        var rst = await _documentNoService.GetNextDocumentNoAsync(new Contracts.DTOs.SystemDtos.DocumentNoRequestDto
        {
            DocType = DocumentTypeStaticEnums.PurchaseOrder,
            BusinessDate = DateTime.Now,
            SerialWidth = 4,
            Operator = "system"
        });

        if (rst.Success && rst.Data != null && !string.IsNullOrWhiteSpace(rst.Data.DocumentNo))
            return rst.Data.DocumentNo;

        // 中文注释：降级兜底，保证流程不中断。
        return $"PO{DateTime.Now:yyyyMMddHHmmssfff}";
    }

    /// <summary>
    /// 按 SKU 获取默认供应商编码。
    /// </summary>
    private static string GetSupplierCodeBySku(string sku, Dictionary<string, string> supplierBySku)
    {
        if (string.IsNullOrWhiteSpace(sku) || supplierBySku == null)
            return string.Empty;

        if (!supplierBySku.TryGetValue(sku, out string? supplierCode))
            return string.Empty;

        return (supplierCode ?? string.Empty).Trim();
    }

    /// <summary>
    /// 解析需求池行供应商：优先取行内手工指定，其次取产品默认供应商。
    /// </summary>
    private static string ResolveSupplierCode(DependentDemand pool, Dictionary<string, string> defaultSupplierBySku)
    {
        if (!string.IsNullOrWhiteSpace(pool.SupplierCode))
            return pool.SupplierCode.Trim();

        if (string.IsNullOrWhiteSpace(pool.Sku))
            return string.Empty;

        return GetSupplierCodeBySku(pool.Sku, defaultSupplierBySku);
    }

    /// <summary>
    /// 按 SKU 解析采购单价（优先产品人民币单价）。
    /// </summary>
    private static decimal ResolveUnitPriceBySku(string sku, Dictionary<string, Product> productBySku)
    {
        if (string.IsNullOrWhiteSpace(sku) || productBySku == null)
            return 0m;

        if (!productBySku.TryGetValue(sku, out Product? product) || product == null)
            return 0m;

        return product.CNYPrice ?? 0m;
    }

}
