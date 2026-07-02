using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 采购订单应用服务。
/// 默认实现了订单头的基础CRUD服务
/// </summary>
public partial class PurchaseOrderService(
    IPurchaseOrderRepository headRepository,
    IPurchaseOrderLineRepository lineRepository,
    ISupplierRepository supplierRepository,
    IProductRepository productRepository,
    IDocumentNoService documentNoService,
    IRepository<FollowUpLog> followUpRepository,
    IDependentDemandRepository dependentDemandRepository,
    ServiceBaseContext context)
    : ApplicationServiceBase(context), IPurchaseOrderService
{
    private readonly ServiceCrud<PurchaseOrderHead, PurchaseOrderHeadDto> _crud = new(headRepository, context);
    private readonly IPurchaseOrderRepository _headRepository = headRepository;
    private readonly IPurchaseOrderLineRepository _lineRepository = lineRepository;
    private readonly ISupplierRepository _supplierRepo = supplierRepository;
    private readonly IProductRepository _pdRepo = productRepository;
    private readonly IDocumentNoService _doc = documentNoService;
    private readonly IRepository<FollowUpLog> _followUpRepository = followUpRepository;
    private readonly IDependentDemandRepository _dependentDemandRepository = dependentDemandRepository;


    #region ======Get======
    /// <summary>
    /// 分页获取订单头
    /// </summary>
    /// <param name="search"></param>
    /// <returns></returns>
    public async Task<ServiceResult<PagedResultDto<PurchaseOrderHeadDto>>> GetPagedListAsync(PurchaseOrderSearchDto search)
    {
        search ??= new PurchaseOrderSearchDto();

        var criteria = new QueryCriteria();
        if (!string.IsNullOrWhiteSpace(search.OrderNo))
            criteria.Add("OrderNo", 查询操作符.模糊匹配, search.OrderNo.Trim());
        if (!string.IsNullOrWhiteSpace(search.PoNo))
            criteria.Add("PoNo", 查询操作符.模糊匹配, search.PoNo.Trim());
        if (!string.IsNullOrWhiteSpace(search.SalesOrderNo))
            criteria.Add("SalesOrderNo", 查询操作符.模糊匹配, search.SalesOrderNo.Trim());
        if (!string.IsNullOrWhiteSpace(search.SupplierCode))
            criteria.Add("SupplierCode", 查询操作符.等于, search.SupplierCode.Trim());
        if (search.OrderStatus >= 0)
            criteria.Add("OrderStatus", 查询操作符.等于, search.OrderStatus);

        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            criteria.AddOrGroup(
                ("OrderNo", 查询操作符.模糊匹配, keyword),
                ("PoNo", 查询操作符.模糊匹配, keyword),
                ("SalesOrderNo", 查询操作符.模糊匹配, keyword),
                ("SupplierCode", 查询操作符.模糊匹配, keyword),
                ("SupplierName", 查询操作符.模糊匹配, keyword));
        }

        var pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
        var pageSize = search.PageSize > 0 ? search.PageSize : 50;
        var paged = await _headRepository.GetPagedListAsync(criteria, pageIndex, pageSize, "Id DESC");

        return ServiceResult<PagedResultDto<PurchaseOrderHeadDto>>.Ok(new PagedResultDto<PurchaseOrderHeadDto>
        {
            Items = paged.Items.Adapt<List<PurchaseOrderHeadDto>>(),
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }

    /// <summary>
    /// 根据采购单ID获取采购单详情，包括头信息和行信息。
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByHeadIdAsync(Guid id)
    {
        var header = await _headRepository.GetByIdAsync(id);
        if (header == null)
            return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单不存在");

        return await BuildDetailByOrderNoAsync(header.OrderNo);
    }


    /// <summary>
    /// 根据采购单号获取采购单详情，包括头信息和行信息。
    /// </summary>
    /// <param name="orderNo">采购单编号</param>
    /// <returns>采购单详情</returns>
    public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo))
            return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单号不能为空");

        return await BuildDetailByOrderNoAsync(orderNo.Trim());
    }

    /// <summary>
    /// 根据自定义编号获取采购单详情，包括头信息和行信息。
    /// </summary>
    /// <param name="boNo">自定义编号</param>
    /// <returns>采购单详情</returns>
    public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByBoNoAsync(string boNo)
    {
        if (string.IsNullOrWhiteSpace(boNo))
            return ServiceResult<PurchaseOrderDetailDto>.Fail("自定义采购编号不能为空");

        var header = await _headRepository.GetByBoNoAsync(boNo);
        if (header == null)
            return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单不存在");

        return await BuildDetailByOrderNoAsync(header.OrderNo); ;
    }

    public async Task<ServiceResult<List<SupplierDto>>> GetAwaitingInboundSuppliersAsync()
    {
        try
        {

            var rst = await _lineRepository.GetAwaitingInboundSuppliersAsync();
            return ServiceResult<List<SupplierDto>>.Ok(rst);

        }
        catch (Exception ex)
        {
            return ServiceResult<List<SupplierDto>>.Fail(ex.Message);

        }
    }
    #endregion


    #region ======Post======

    /// <summary>
    /// 保存一张采购单，包括头信息和行信息。支持新增和更新操作。
    /// </summary>
    /// <param name="request">采购单详情请求对象</param>
    /// <returns>成功返回成功Dto,否则返回错误值</returns>
    public async Task<ServiceResult<Guid>> SaveDetailAsync(PurchaseOrderDetailDto request)
    {
        //========基础校验========
        if (request == null || request.Header == null || request.Lines == null || request.Lines.Count == 0)
            return ServiceResult<Guid>.Fail("采购单头和行不能为空");


        //如果订单头 ID 大于 0，视为更新操作；否则视为创建操作
        if (request.Header.Uid != Guid.Empty)
        {
            var updateRst = await SaveAsUpdateAsync(request);
            return updateRst.Success
                ? ServiceResult<Guid>.Ok(updateRst.Data)
                : ServiceResult<Guid>.Fail(updateRst.Message);
        }
        //创建操作
        return await SaveAsCreateAsync(request);

    }


    #endregion




    /// <summary>
    /// 创建一张订单（含行）事务。
    /// </summary>
    private async Task<ServiceResult<Guid>> SaveAsCreateAsync(PurchaseOrderDetailDto request)
    {
        if (request?.Header == null)
            return ServiceResult<Guid>.Fail("订单头不能为空");

        var headerEntity = request.Header.Adapt<PurchaseOrderHead>();

        if (string.IsNullOrWhiteSpace(headerEntity.OrderNo))
            return ServiceResult<Guid>.Fail("采购单号不能为空");
        if (string.IsNullOrWhiteSpace(headerEntity.SupplierCode))
            return ServiceResult<Guid>.Fail("供应商不能为空");


        DecomposeCache cache = await 构建缓存Async(request.Lines.Select(x => x.Sku), [request.Header.SupplierCode]);
        Supplier? supplier = cache.GetSupplierByCode(request.Header.SupplierCode);

        if (supplier == null)
            return ServiceResult<Guid>.Fail("供应商不存在");

        var vladRst = 核对采购单行是否符合规则(request.Lines, cache);
        if (!vladRst.Success)
            return ServiceResult<Guid>.Fail(vladRst.Message);

        //补全默认值
        headerEntity.BoNo = string.IsNullOrWhiteSpace(headerEntity.BoNo) ? null : headerEntity.BoNo.Trim();
        headerEntity.IsDeleted = false;
        headerEntity.IsLocked = false;
        headerEntity.OrderStatus = PurchaseOrderStatusEnum.未确认;
        headerEntity.ConfirmationStatus = ConfirmationStatusEnum.待确认;

        headerEntity.LineCount = request.Lines.Count;
        headerEntity.TotalShippedQty = 0;
        headerEntity.TotalUnshippedQty = 0;

        //如果客户名称为空,则从缓存中解析供应商名称并补全（如果解析不到名称，则保持原值不变，允许后续人工维护）
        headerEntity.SupplierName = string.IsNullOrWhiteSpace(headerEntity.SupplierName)
            ? cache.ResolveSupplierName(supplier.SupplierCode)
            : headerEntity.SupplierName;
        headerEntity.Currency = string.IsNullOrWhiteSpace(headerEntity.Currency) ? supplier.Currency : headerEntity.Currency;


        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            //获取一个新的PO单号（如果请求中没有提供的话）
            if (string.IsNullOrWhiteSpace(headerEntity.OrderNo))
            {
                var newOrderNo = await _doc.GetNextDocumentNoAsync(new DocumentNoRequestDto { DocType = DocumentTypeStaticsEnum.PurchaseOrder });
                if (!newOrderNo.Success)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<Guid>.Fail(newOrderNo.Message);
                }
                headerEntity.OrderNo = newOrderNo.Data.DocumentNo;
            }

            var existingHeaders = await _headRepository.GetListAsync(new { OrderNo = headerEntity.OrderNo.Trim() });
            if (existingHeaders != null && existingHeaders.Count > 0)
                return ServiceResult<Guid>.Fail("采购单号已存在，请更换单号后重试");

            //新增到数据库并获取新生成的订单ID
            Guid newId = await _headRepository.AddAsync(headerEntity);

            //=========在新增采购单行的时候,需要尝试去净需求里找到对应的记录,把采购单行Id回写到净需求里,以建立订单行和净需求的关联关系,方便后续根据订单行去跟踪净需求的执行状态========
            // 2. 准备采购行数据与映射关系
            var lineItems = new List<PurchaseOrderLine>();
            var lineToDemandMapping = new List<(PurchaseOrderLine Entity, List<Guid> DemandIds)>();

            int lineNumber = 0;
            foreach (var lineDto in request.Lines)
            {
                var lineEntity = lineDto.Adapt<PurchaseOrderLine>();
                lineEntity.PurchaseOrderId = newId;
                lineEntity.OrderNo = headerEntity.OrderNo;
                lineEntity.LineNumber = lineNumber++;
                lineEntity.IsDeleted = false;

                lineItems.Add(lineEntity);

                if (lineDto.DemandIdList?.Count > 0)
                    lineToDemandMapping.Add((lineEntity, lineDto.DemandIdList));
            }

            // 3. 【核心】先保存采购行，利用 AddRangeAsync 回写 lineEntity.Id
            if (lineItems.Count > 0)
            {
                await _lineRepository.AddRangeAsync(lineItems);
            }

            // 4. 【倒推重写】现在 lineEntity.Id 有值了，更新净需求
            var allDemandIds = lineToDemandMapping.SelectMany(m => m.DemandIds).Distinct().ToList();
            if (allDemandIds.Count > 0)
            {
                var demands = await _dependentDemandRepository.GetByIdListAsync(allDemandIds, true);
                var demandDict = demands.ToDictionary(d => d.Uid, d => d);

                foreach (var mapping in lineToDemandMapping)
                {
                    foreach (var dId in mapping.DemandIds)
                    {
                        if (demandDict.TryGetValue(dId, out var demand))
                        {
                            demand.TargetNo = headerEntity.OrderNo;
                            demand.TargetLineId = mapping.Entity.Uid; // 使用新生成的 ID
                            demand.OrderStatus = DependentDemandStatusEnum.已转为正式单据;                            
                            demand.ExecutedQty = demand.NetQty; // 标记全部执行
                        }
                    }
                }
                await _dependentDemandRepository.UpdateRangeAsync(demands);
            }

            _session.Transaction.Commit();
            return ServiceResult<Guid>.Ok(newId);
        }
        catch
        {
            _session.Transaction.Rollback();
            throw;
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }


    /// <summary>
    /// 更新一张订单（含行）事务。
    /// </summary>
    private async Task<ServiceResult<Guid>> SaveAsUpdateAsync(PurchaseOrderDetailDto request)
    {
        if (request?.Header == null)
            return ServiceResult<Guid>.Fail("订单头不能为空");
        if (request.Header.ApprovalStatus == ApprovalStatusEnum.已批准)
            return ServiceResult<Guid>.Fail("已批准的订单不允许修改");

        var headerEntity = request.Header.Adapt<PurchaseOrderHead>();

        if (string.IsNullOrWhiteSpace(headerEntity.OrderNo))
            return ServiceResult<Guid>.Fail("采购单号不能为空");
        if (string.IsNullOrWhiteSpace(headerEntity.SupplierCode))
            return ServiceResult<Guid>.Fail("供应商不能为空");


        DecomposeCache cache = await 构建缓存Async(request.Lines.Select(x => x.Sku), [request.Header.SupplierCode]);
        Supplier? supplier = cache.GetSupplierByCode(request.Header.SupplierCode);

        if (supplier == null)
            return ServiceResult<Guid>.Fail("供应商不存在");

        var vladRst = 核对采购单行是否符合规则(request.Lines, cache);
        if (!vladRst.Success)
            return ServiceResult<Guid>.Fail(vladRst.Message);

        // 中文注释：PoNo 为用户自定义编号，允许为空；若有值仅作展示，不参与主链路关联。
        if (!string.IsNullOrWhiteSpace(headerEntity.BoNo))
            headerEntity.BoNo = headerEntity.BoNo.Trim();


        // 中文注释：若请求未显式传币种，则从供应商档案补默认币种，避免前端二次查档案。
        if (string.IsNullOrWhiteSpace(headerEntity.Currency))
        {
            headerEntity.Currency = headerEntity.Currency == null ? headerEntity.Currency : supplier.Currency;
        }

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            //校验订单行数据是否合法（如果有订单行的话）
            //var checkrst = await CheckLineListCanBeDeleteOrUpdatedAsync(OrderTypesEnum.销售订单, lines.Select(l => l.Id));//检查订单行是否都允许被修改或删除（如果有订单行被修改或删除的话）

            //校验通过
            //if (!checkrst.Success)
            //{
            //    _session.Transaction.Rollback();
            //    return ServiceResult<PurchaseOrderDetailDto>.Fail(checkrst.Message);
            //}

            //首先获取数据库中订单头信息，如果不存在则回滚并返回失败结果
            var existingHeader = await _headRepository.GetByIdAsync(headerEntity.Uid, true);
            if (existingHeader == null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<Guid>.Fail("订单不存在");
            }

            // 1. 处理删除行 (释放净需求)
            if (request.DeletedLineIds?.Count > 0)
            {
                // 在物理删除采购行之前，先找到指向这些行的净需求并清空关联
                await _dependentDemandRepository.ResetByTargetLineIdsAsync(request.DeletedLineIds);
                await _lineRepository.DeleteRangeAsync(request.DeletedLineIds, true);
            }
            // 2. 区分新增和更新行
            var addLines = new List<(PurchaseOrderLine Entity, List<Guid> DemandIds)>();
            var updateLines = new List<PurchaseOrderLine>();
            foreach (var lineDto in request.Lines)
            {

                var lineEntity = lineDto.Adapt<PurchaseOrderLine>();

                lineEntity.OrderNo = headerEntity.OrderNo;
                lineEntity.PurchaseOrderId = headerEntity.Uid;
                lineEntity.IsDeleted = false;

                if (lineEntity.Uid == Guid.Empty)
                    addLines.Add((lineEntity, lineDto.DemandIdList));
                else
                    updateLines.Add(lineEntity);
            }

            // 3. 处理新增行 (逻辑同 Create)
            if (addLines.Count > 0)
            {
                var entities = addLines.Select(x => x.Entity).ToList();
                await _lineRepository.AddRangeAsync(entities);

                // 回写新行的需求关联... (参考 Create 逻辑)
            }

            // 4. 处理更新行
            if (updateLines.Count > 0)
            {
                //获取旧的订单行数据，并按照订单行ID建立一个字典，方便后续对比每行的ReadyQty是否有变更，以及如果关联了净需求的话，计算ReadyQty的增量值（delta），准备后续同步更新回净需求的ReceivedQty字段。
                var oldLines = await _lineRepository.GetByIdListAsync([.. updateLines.Select(x => x.Uid)], true);
                if (oldLines == null || oldLines.Count != updateLines.Count)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<Guid>.Fail("部分订单行不存在，无法更新");
                }
                var dictOldLines = oldLines.ToDictionary(x => x.Uid, x => x);
                //准备一个列表来收集所有完工数量有变更的订单行，以及对应的跟进日志记录，后续批量新增跟进日志和批量更新订单行。
                var readyQtyChangedLines = new List<PurchaseFollowUpUpdateDto>();
                //跟进日志
                var flogList = new List<FollowUpLog>();
                //净需求更新列表
                var dependNeedToUpdate = await _dependentDemandRepository.GetListByTargetLineIdsAsync([.. updateLines.Select(l => l.Uid)], true);

                //遍历每一行更新数据，检查完工数量是否有变更，如果有变更则记录跟进日志，并且如果关联了净需求的话，计算增量值并同步更新回净需求的ReceivedQty字段。
                foreach (var line in updateLines)
                {
                    if (dictOldLines.TryGetValue(line.Uid, out var oldEntity))
                    {
                        var delta = line.ReadyQty - oldEntity.ReadyQty;

                        if (line.ReadyQty != oldEntity.ReadyQty
                            || line.FollowExpectedDate?.Date != oldEntity.FollowExpectedDate?.Date
                            || !string.Equals(line.FollowUpRemarks, oldEntity.FollowUpRemarks, StringComparison.OrdinalIgnoreCase))
                        {
                            var changes = new List<string>();

                            if (line.ReadyQty != oldEntity.ReadyQty)
                                changes.Add($"订单行 {line.LineNumber} 的完工数量由 {oldEntity.ReadyQty} 变更为 {line.ReadyQty}");

                            if (line.FollowExpectedDate != oldEntity.FollowExpectedDate)
                                changes.Add($"跟进预期日期由 {oldEntity.FollowExpectedDate:yyyy-MM-dd} 变更为 {line.FollowExpectedDate:yyyy-MM-dd}");

                            if (!string.Equals(line.FollowUpRemarks, oldEntity.FollowUpRemarks, StringComparison.OrdinalIgnoreCase))
                                changes.Add($"跟进备注由 {oldEntity.FollowUpRemarks} 变更为 {line.FollowUpRemarks}");

                            string noteContent = string.Join(Environment.NewLine, changes);

                            flogList.Add(new FollowUpLog
                            {
                                RefLineId = line.Uid,
                                NoteContent = noteContent,
                                CreatedDate = DateTime.Now,
                                IsDeleted = false
                            });
                            line.FollowUpTime = DateTime.Now;
                        }
                        // 中文注释：若采购行关联到净需求，则按 ReadyQty 的增量同步回写 ReceivedQty。
                        // 业务语义：ReceivedQty 作为过程进度累计值，允许正向增加，也允许回调时减少（delta 可能为负）。
                        foreach (var dep in dependNeedToUpdate.Where(d => d.TargetLineId == line.Uid))
                        {
                            if (dep != null && !dep.IsDeleted && line.ReadyQty != oldEntity.ReadyQty)
                            {
                                dep.ReadyQty = line.ReadyQty;
                            }
                        }
                    }

                }

                //如果有完工数量变更的订单行，则批量新增跟进日志
                if (flogList.Count > 0)
                {
                    foreach (var followUpLog in flogList)
                    {
                        await _followUpRepository.AddAsync(followUpLog);
                    }
                }
                //如果有订单行关联的净需求需要更新，则批量更新净需求
                if (dependNeedToUpdate.Count > 0)
                {
                    await _dependentDemandRepository.UpdateRangeAsync(dependNeedToUpdate);
                }

                headerEntity.TotalReadyQty = updateLines.Sum(x => x.ReadyQty);
                //更新订单
                await _headRepository.UpdateAsync(headerEntity);
                await _lineRepository.UpdateRangeAsync(updateLines);
            }

            _session.Transaction.Commit();
            return ServiceResult<Guid>.Ok(headerEntity.Uid);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<Guid>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    private static ServiceResult<bool> 核对采购单行是否符合规则(IEnumerable<PurchaseOrderLineDto> purchaseOrderLines, DecomposeCache cache)
    {

        foreach (var line in purchaseOrderLines)
        {
            if (line.QTY <= 0)
                return ServiceResult<bool>.Fail($"行 {line.Sku} 的数量必须大于0");
            if (line.UnitPrice < 0)
                return ServiceResult<bool>.Fail($"行 {line.Sku} 的单价不能为负数");
            var pd = cache.GetProductBySku(line.Sku);
            if (pd != null && pd.MaterialType == MaterialTypesEnum.客供件 && string.IsNullOrWhiteSpace(line.DeliveryTo))
                return ServiceResult<bool>.Fail($"行 {line.Sku} 的物料类型为客供件，请为客供件指定送货地点");
            // 可以根据业务规则添加更多的校验逻辑
        }
        return ServiceResult<bool>.Ok(true);
    }


    /// <summary>
    /// 构建采购单详情数据，包含头信息和行信息。
    /// </summary>
    /// <param name="orderNo">采购单编号</param>
    /// <returns>采购单详情</returns>
    private async Task<ServiceResult<PurchaseOrderDetailDto>> BuildDetailByOrderNoAsync(string orderNo)
    {
        var header = await _headRepository.GetByOrderNoAsync(orderNo);
        if (header == null)
            return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单不存在");

        var lines = await _lineRepository.GetReadOnlyLinesByPurchaseOrderIdAsync(header.Uid);
        var detail = new PurchaseOrderDetailDto
        {
            Header = header.Adapt<PurchaseOrderHeadDto>(),
            Lines = lines
        };
        return ServiceResult<PurchaseOrderDetailDto>.Ok(detail);
    }


    /// <summary>
    /// 构建需求缓存。
    /// 目标：频繁访问的 Product/BOM/Supplier 统一拉到内存，避免 N+1 查询。
    /// </summary>
    /// <param name="skus">需要参与拆解的根 SKU 集合（可跨来源单据）。</param>
    private async Task<DecomposeCache> 构建缓存Async(IEnumerable<string> skus, IEnumerable<string> codeList)
    {
        var pds = await _pdRepo.GetBySkuListAsync(skus);
        var sps = await _supplierRepo.GetBySupplierCodeListAsync(codeList);

        return new DecomposeCache(pds, sps);
    }





    /// <summary>
    /// 运行时缓存对象。
    /// </summary>
    private sealed class DecomposeCache
    {
        private readonly Dictionary<string, Product> _productBySku;
        private readonly Dictionary<string, Supplier> _supplierByCode;

        public DecomposeCache(IEnumerable<Product> products, IEnumerable<Supplier> suppliers)
        {
            _productBySku = products
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                .GroupBy(x => x.Sku!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            _supplierByCode = suppliers
                .Select(x => new { Supplier = x, SupplierCode = x.SupplierCode?.Trim() })
                .Where(x => !string.IsNullOrWhiteSpace(x.SupplierCode))
                .GroupBy(x => x.SupplierCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Supplier, StringComparer.OrdinalIgnoreCase);
        }

        public Product? GetProductBySku(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku)) return null;
            _productBySku.TryGetValue(sku, out var product);
            return product;
        }
        public Supplier? GetSupplierByCode(string supplierCode)
        {
            if (string.IsNullOrWhiteSpace(supplierCode)) return null;
            _supplierByCode.TryGetValue(supplierCode, out var supplier);
            return supplier;
        }


        public string ResolveSupplierName(string? supplierCode)
        {
            if (string.IsNullOrWhiteSpace(supplierCode)) return "未知供应商";
            if (!_supplierByCode.TryGetValue(supplierCode, out var supplier)) return supplierCode;
            return string.IsNullOrWhiteSpace(supplier.SupplierName) ? supplierCode : supplier.SupplierName;
        }
    }
}
