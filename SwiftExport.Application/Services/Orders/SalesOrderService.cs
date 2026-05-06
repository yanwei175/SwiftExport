using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单应用服务实现。
/// </summary>
public class SalesOrderService(
    ISalesOrderRepository salesOrderRepository,
    IRepository<SalesOrderHeader> headerRepository,
    IRepository<SalesOrderLine> lineRepository,
    IRepository<Customer> customerRepository,
    ISalesOrderLineSourceService salesOrderLineSourceService,
    IDocumentNoService documentNoService,
    IIndependentDemandRepository salesDemandRepository,
    ServiceBaseContext context)
    : BaseService<SalesOrderHeader, SalesOrderDto>(headerRepository, context), ISalesOrderService
{
    private readonly ISalesOrderRepository _salesOrderRepo = salesOrderRepository;
    private readonly IRepository<SalesOrderLine> _lineRepository = lineRepository;
    private readonly IRepository<Customer> _customerRepository = customerRepository;
    private readonly ISalesOrderLineSourceService _salesOrderLineSourceService = salesOrderLineSourceService;
    private readonly IDocumentNoService _documentNoService = documentNoService;
    private readonly IIndependentDemandRepository _demandRepo = salesDemandRepository;
    #region 兼容旧接口（已有）

    /// <summary>
    /// 中文注释：分页查询销售订单列表（旧 DTO，保留兼容）。
    /// </summary>
    public async Task<ServiceResult<PagedResultDto<SalesOrderDto>>> GetSalesOrderPagedListAsync(SalesOrderSearchDto search)
    {
        var criteria = BuildLegacySearchCriteria(search);
        int pageIndex = search?.PageIndex > 0 ? search.PageIndex : 1;
        int pageSize = search?.PageSize > 0 ? search.PageSize : 50;

        string sortBy = string.IsNullOrWhiteSpace(search?.SortBy) ? "OrderDate" : search.SortBy;
        string orderBy = $"{sortBy} {(search?.SortAscending == true ? "ASC" : "DESC")}";

        var paged = await _repository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
        var customerNameMap = await BuildCustomerNameMapAsync();

        var dto = new PagedResultDto<SalesOrderDto>
        {
            Items = [.. paged.Items.Select(x => MapToSalesOrderDto(x, customerNameMap))],
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        };

        return ServiceResult<PagedResultDto<SalesOrderDto>>.Ok(dto);
    }

    /// <summary>
    /// 中文注释：获取销售订单详情（订单头 + 明细行）。
    /// </summary>
    public async Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailAsync(int orderId)
    {
        var header = await _repository.GetByIdAsync(orderId);
        if (header == null)
            return ServiceResult<SalesOrderDetailDto>.Fail("未找到销售订单");

        var lines = await GetLinesByOrderNoAsync(header.OrderNo);

        return ServiceResult<SalesOrderDetailDto>.Ok(new SalesOrderDetailDto
        {
            Header = header.Adapt<SalesOrderHeaderDto>(),
            Lines = lines.Adapt<List<SalesOrderLineDto>>()
        });
    }

    /// <summary>
    /// 中文注释：创建销售订单。
    /// </summary>
    public async Task<ServiceResult<int>> CreateSalesOrderAsync(SalesOrderDetailDto request)
    {
        return await SaveAsCreateAsync(request);
    }

    /// <summary>
    /// 中文注释：更新销售订单。
    /// </summary>
    public async Task<ServiceResult<bool>> UpdateSalesOrderAsync(int orderId, SalesOrderDetailDto request)
    {
        var rst = await SaveAsUpdateAsync(orderId, request);
        return rst.Success
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.Fail(rst.Message);
    }

    /// <summary>
    /// 中文注释：删除单个销售订单（同步删除明细）。
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteSalesOrderAsync(int orderId)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var header = await _repository.GetByIdAsync(orderId);
            if (header == null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail("订单不存在");
            }

            var lines = await GetLinesByOrderNoAsync(header.OrderNo);
            if (lines.Count > 0)
            {
                await _lineRepository.DeleteRangeAsync(lines.Select(x => x.Id));
            }

            await _repository.DeleteAsync(orderId);
            _session.Transaction.Commit();
            return ServiceResult<bool>.Ok(true);
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
    /// 中文注释：批量删除销售订单（逐单级联删除明细）。
    /// </summary>
    public async Task<ServiceResult<bool>> BatchDeleteSalesOrderAsync(List<int> orderIds)
    {
        if (orderIds == null || orderIds.Count == 0)
            return ServiceResult<bool>.Ok(true);

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            foreach (var orderId in orderIds.Distinct())
            {
                var header = await _repository.GetByIdAsync(orderId);
                if (header == null) continue;

                var lines = await GetLinesByOrderNoAsync(header.OrderNo);
                if (lines.Count > 0)
                {
                    await _lineRepository.DeleteRangeAsync(lines.Select(x => x.Id));
                }

                await _repository.DeleteAsync(orderId);
            }

            _session.Transaction.Commit();
            return ServiceResult<bool>.Ok(true);
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

    #endregion

    #region 订单管理页（主从表）

    /// <summary>
    /// 中文注释：分页查询订单头列表（用于主表）。
    /// </summary>
    public async Task<ServiceResult<PagedResultDto<SalesOrderHeaderDto>>> GetSalesOrderHeaderPagedListAsync(SalesOrderManagerSearchDto search)
    {
        var criteria = await BuildManagerSearchCriteriaAsync(search);
        int pageIndex = search?.PageIndex > 0 ? search.PageIndex : 1;
        int pageSize = search?.PageSize > 0 ? search.PageSize : 50;

        string sortBy = string.IsNullOrWhiteSpace(search?.SortBy) ? "OrderDate" : search.SortBy;
        string orderBy = $"{sortBy} {(search?.SortAscending == true ? "ASC" : "DESC")}";

        var paged = await _repository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);

        return ServiceResult<PagedResultDto<SalesOrderHeaderDto>>.Ok(new PagedResultDto<SalesOrderHeaderDto>
        {
            Items = paged.Items.Adapt<List<SalesOrderHeaderDto>>(),
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }

    /// <summary>
    /// 中文注释：根据订单 ID 获取订单行列表。
    /// </summary>
    public async Task<ServiceResult<List<SalesOrderLineDto>>> GetSalesOrderLinesAsync(int orderId)
    {
        var header = await _repository.GetByIdAsync(orderId);
        if (header == null)
            return ServiceResult<List<SalesOrderLineDto>>.Fail("订单不存在");

        var lines = await GetLinesByOrderNoAsync(header.OrderNo);
        return ServiceResult<List<SalesOrderLineDto>>.Ok(lines.Adapt<List<SalesOrderLineDto>>());
    }

    /// <summary>
    /// 中文注释：订单管理页保存接口（头批量 + 当前订单行批量）。
    /// </summary>
    public async Task<ServiceResult<bool>> SaveSalesOrderManagerAsync(SalesOrderManagerSaveDto request)
    {
        request ??= new SalesOrderManagerSaveDto();

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 中文注释：处理订单头新增。
            if (request.HeaderChanges?.AddList?.Count > 0)
            {
                var addHeaders = request.HeaderChanges.AddList.Adapt<List<SalesOrderHeader>>();
                foreach (var item in addHeaders)
                {
                    item.CreatedDate ??= DateTime.Now;
                    item.IsDeleted = false;
                    item.OrderNo = item.OrderNo;
                }
                await _repository.AddRangeAsync(addHeaders);
            }

            // 中文注释：处理订单头更新。
            if (request.HeaderChanges?.UpdateList?.Count > 0)
            {
                var updateHeaders = request.HeaderChanges.UpdateList.Adapt<List<SalesOrderHeader>>();
                foreach (var item in updateHeaders)
                {
                    item.UpdatedDate = DateTime.Now;
                    item.OrderNo = item.OrderNo;
                }
                await _repository.UpdateRangeAsync(updateHeaders);
            }

            // 中文注释：处理订单头删除（级联删除行）。
            if (request.HeaderChanges?.DeleteIdList?.Count > 0)
            {
                foreach (var deleteHeaderId in request.HeaderChanges.DeleteIdList.Distinct())
                {
                    var header = await _repository.GetByIdAsync(deleteHeaderId);
                    if (header == null) continue;

                    var lines = await GetLinesByOrderNoAsync(header.OrderNo);
                    if (lines.Count > 0)
                    {
                        await _lineRepository.DeleteRangeAsync(lines.Select(x => x.Id));
                    }
                }

                await _repository.DeleteRangeAsync([.. request.HeaderChanges.DeleteIdList.Distinct()]);
            }

            // 中文注释：处理当前选中订单的行批量变更。
            if (request.CurrentOrderId.HasValue && request.LineChanges != null)
            {
                var header = await _repository.GetByIdAsync(request.CurrentOrderId.Value);
                if (header == null)
                    return ServiceResult<bool>.Fail("当前订单不存在，无法保存明细行");

                string orderNo = header.OrderNo;

                if (request.LineChanges.AddList?.Count > 0)
                {
                    var addLines = request.LineChanges.AddList.Adapt<List<SalesOrderLine>>();
                    foreach (var line in addLines)
                    {
                        line.OrderNo = orderNo;
                        line.CreatedDate ??= DateTime.Now;
                        line.IsDeleted = false;
                    }
                    await _lineRepository.AddRangeAsync(addLines);
                }

                if (request.LineChanges.UpdateList?.Count > 0)
                {
                    var updateLines = request.LineChanges.UpdateList.Adapt<List<SalesOrderLine>>();
                    foreach (var line in updateLines)
                    {
                        line.OrderNo = orderNo;
                        line.UpdatedDate = DateTime.Now;
                    }
                    await _lineRepository.UpdateRangeAsync(updateLines);
                }

                if (request.LineChanges.DeleteIdList?.Count > 0)
                {
                    await _lineRepository.DeleteRangeAsync([.. request.LineChanges.DeleteIdList.Distinct()]);
                }
            }

            _session.Transaction.Commit();
            return ServiceResult<bool>.Ok(true);
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

    #endregion

    #region 订单详情页（单据编辑）

    /// <summary>
    /// 中文注释：详情页一键保存（Header.Id > 0 视为更新，否则视为新增）。
    /// </summary>
    public async Task<ServiceResult<int>> SaveSalesOrderDetailAsync(SalesOrderDetailDto request)
    {
        //如果订单头信息不存在，直接返回失败结果
        if (request?.Header == null)
            return ServiceResult<int>.Fail("订单头不能为空");
        //如果订单头 ID 大于 0，视为更新操作；否则视为创建操作
        if (request.Header.Id > 0)
        {
            var updateRst = await SaveAsUpdateAsync(request.Header.Id, request);
            return updateRst.Success
                ? ServiceResult<int>.Ok(request.Header.Id)
                : ServiceResult<int>.Fail(updateRst.Message);
        }
        //创建操作
        return await SaveAsCreateAsync(request);
    }

    /// <summary>
    /// 中文注释：销售订单行数据源分页查询。
    /// 实际查询由专用 SalesOrderLineSourceService 承担，
    /// 这里保持 ISalesOrderService 对外能力完整，便于 Controller 与 Refit 统一调用。
    /// </summary>
    public async Task<ServiceResult<PagedResultDto<SalesOrderLineDto>>> GetSalesOrderLineSourcePagedListAsync(SalesOrderLineSourceSearchDto search)
    {
        return await _salesOrderLineSourceService.GetPagedSalesOrderLineSourceDtoAsync(search);
    }

    /// <summary>
    /// 中文注释：获取销售订单下一单号。
    /// </summary>
    public async Task<ServiceResult<DocumentNoResultDto>> GetNextOrderNoAsync(DocumentNoRequestDto request)
    {
        request ??= new DocumentNoRequestDto();
        request.DocType = DocumentTypeStaticsEnum.SalesOrder;
        return await _documentNoService.GetNextDocumentNoAsync(request);
    }

    /// <summary>
    /// 批准销售订单。
    /// </summary>
    /// <param name="orderId">订单ID</param>
    /// <returns>操作结果</returns>
    public async Task<ServiceResult<bool>> ApproveSalesOrderAsync(int orderId)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 1. 获取并锁定数据
            var order = await _repository.GetByIdAsync(orderId);
            if (order == null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail("订单不存在");
            }

            // 2. 基础状态校验只能是 草稿 或 待审批 状态。
            if (order.ApprovalStatus != ApprovalStatusEnum.草稿 && order.ApprovalStatus != ApprovalStatusEnum.待审批)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail("订单状态不合法，只有草稿或待审批状态的订单才能审批");
            }

            // 中文注释：审批前执行逐行业务校验，避免遗漏后续异常行。
            var orderLines = await GetLinesByOrderNoAsync(order.OrderNo);
            var validateRst = await ValidateBusinessRulesAsync(order, orderLines);
            if (!validateRst.Success)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail(validateRst.Message);
            }

            // 3. 同步该订单对应销售需求状态为“待处理”（审批通过后由 IndependentDemand 驱动后续拆解）。
            var updateDemandRst = await UpdateDemandStatusBySourceNoAsync(OrderTypesEnum.销售订单, order.OrderNo ?? string.Empty, IndependentDemandStatusEnum.已确认);
            if (!updateDemandRst.Success)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail(updateDemandRst.Message);
            }

            // 4. 更新订单审批状态。
            order.ApprovalStatus = ApprovalStatusEnum.已批准;
            order.UpdatedDate = DateTime.Now;
            await _repository.UpdateAsync(order);

            _session.Transaction.Commit();
            return ServiceResult<bool>.Ok(true);
        }
        catch (Exception)
        {
            _session.Transaction.Rollback();
            return ServiceResult<bool>.Fail("审批订单失败");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>
    /// 驳回销售订单
    /// </summary>
    /// <param name="orderId"></param>
    /// <returns></returns>
    public async Task<ServiceResult<bool>> RejectSalesOrderAsync(int orderId)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var order = await _repository.GetByIdAsync(orderId);
            if (order == null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail("订单不存在");
            }

            if (order.ApprovalStatus == ApprovalStatusEnum.已批准)
            {
                // 中文注释：已批准回退前先判断销售需求是否已进入执行态。
                var canRejectRst = await CanRejectApprovedOrderByDemandStatusAsync(OrderTypesEnum.销售订单, order.OrderNo ?? string.Empty);

                if (!canRejectRst.Success || !canRejectRst.Data)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<bool>.Fail(canRejectRst.Message);
                }

                // 中文注释：回退审批时，将销售需求状态恢复为“草稿”，避免未审批订单被执行拆解。
                var rollbackDemandRst = await UpdateDemandStatusBySourceNoAsync(OrderTypesEnum.销售订单, order.OrderNo ?? string.Empty, IndependentDemandStatusEnum.草稿);

                if (!rollbackDemandRst.Success)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<bool>.Fail($"回退销售需求状态失败：{rollbackDemandRst.Message}");
                }
            }

            order.ApprovalStatus = ApprovalStatusEnum.已驳回;
            order.UpdatedDate = DateTime.Now;
            await _repository.UpdateAsync(order);

            _session.Transaction.Commit();
            return ServiceResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<bool>.Fail($"驳回失败：{ex.Message}");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>
    /// 批量批准销售订单
    /// </summary>
    /// <param name="orderIds">订单ID列表</param>
    /// <returns>操作结果</returns>
    public async Task<ServiceResult<List<int>>> BatchApproveSalesOrdersAsync(List<int> orderIds)
    {
        if (orderIds == null || orderIds.Count == 0)
            return ServiceResult<List<int>>.Fail("订单ID列表不能为空");

        var failed = new List<string>();
        foreach (var orderId in orderIds.Distinct())
        {
            var rst = await ApproveSalesOrderAsync(orderId);
            if (!rst.Success)
                failed.Add($"{orderId}:{rst.Message}");
        }

        if (failed.Count > 0)
            return ServiceResult<List<int>>.Fail("部分审批失败：" + string.Join("; ", failed));

        return ServiceResult<List<int>>.Ok(orderIds);
    }
    /// <summary>
    /// 批量驳回销售订单
    /// </summary>
    /// <param name="orderIds">订单ID列表</param>
    /// <returns>操作结果</returns>
    public async Task<ServiceResult<List<string>>> BatchRejectSalesOrdersAsync(List<int> orderIds)
    {
        if (orderIds == null || orderIds.Count == 0)
            return ServiceResult<List<string>>.Fail("订单ID列表不能为空");

        var failed = new List<string>();
        foreach (var orderId in orderIds.Distinct())
        {
            var rst = await RejectSalesOrderAsync(orderId);
            if (!rst.Success)
                failed.Add($"{orderId}:{rst.Message}");
        }

        if (failed.Count > 0)
            return ServiceResult<List<string>>.Fail("部分驳回失败：" + string.Join("; ", failed));

        return ServiceResult<List<string>>.Ok(failed);
    }


    #endregion

    #region 私有辅助方法

    /// <summary>
    /// 中文注释：构造旧分页接口的查询条件。
    /// </summary>
    private static QueryCriteria BuildLegacySearchCriteria(SalesOrderSearchDto search)
    {
        search ??= new SalesOrderSearchDto();
        var criteria = new QueryCriteria();

        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            criteria.AddOrGroup(
                ("OrderNo", 查询操作符.模糊匹配, search.Keyword),
                ("CustomerCode", 查询操作符.模糊匹配, search.Keyword),
                ("InvoiceNo", 查询操作符.模糊匹配, search.Keyword),
                ("Remark", 查询操作符.模糊匹配, search.Keyword)
            );
        }

        if (!string.IsNullOrWhiteSpace(search.OrderNo))
            criteria.Add("OrderNo", 查询操作符.模糊匹配, search.OrderNo);

        if (search.CustomerId.HasValue)
            criteria.Add("CustomerId", 查询操作符.等于, search.CustomerId.Value);

        if (search.OrderDateFrom.HasValue)
            criteria.Add("OrderDate", 查询操作符.大于等于, search.OrderDateFrom.Value);

        if (search.OrderDateTo.HasValue)
            criteria.Add("OrderDate", 查询操作符.小于等于, search.OrderDateTo.Value);

        if (search.DeliveryDateFrom.HasValue)
            criteria.Add("DeliveryDate", 查询操作符.大于等于, search.DeliveryDateFrom.Value);

        if (search.DeliveryDateTo.HasValue)
            criteria.Add("DeliveryDate", 查询操作符.小于等于, search.DeliveryDateTo.Value);

        if (search.OrderStatus>=0)
            criteria.Add("OrderStatus", 查询操作符.等于, search.OrderStatus);

        if (search.TotalAmountFrom.HasValue)
            criteria.Add("TotalAmountLocalCurrency", 查询操作符.大于等于, search.TotalAmountFrom.Value);

        if (search.TotalAmountTo.HasValue)
            criteria.Add("TotalAmountLocalCurrency", 查询操作符.小于等于, search.TotalAmountTo.Value);

        return criteria;
    }

    /// <summary>
    /// 中文注释：构造订单管理页查询条件。
    /// </summary>
    private async Task<QueryCriteria> BuildManagerSearchCriteriaAsync(SalesOrderManagerSearchDto search)
    {
        search ??= new SalesOrderManagerSearchDto();
        var criteria = new QueryCriteria();

        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            criteria.AddOrGroup(
                ("OrderNo", 查询操作符.模糊匹配, search.Keyword),
                ("CustomerCode", 查询操作符.模糊匹配, search.Keyword),
                ("InvoiceNo", 查询操作符.模糊匹配, search.Keyword),
                ("Remark", 查询操作符.模糊匹配, search.Keyword)
            );
        }

        if (!string.IsNullOrWhiteSpace(search.CustomerCode))
            criteria.Add("CustomerCode", 查询操作符.等于, search.CustomerCode);

        if (!string.IsNullOrWhiteSpace(search.OrderNo))
            criteria.Add("OrderNo", 查询操作符.模糊匹配, search.OrderNo);

        if (!string.IsNullOrWhiteSpace(search.InvoiceNo))
            criteria.Add("InvoiceNo", 查询操作符.模糊匹配, search.InvoiceNo);

        if (search.OrderDateFrom.HasValue)
            criteria.Add("OrderDate", 查询操作符.大于等于, search.OrderDateFrom.Value);

        if (search.OrderDateTo.HasValue)
            criteria.Add("OrderDate", 查询操作符.小于等于, search.OrderDateTo.Value);

        if (search.OrderStatuses?.Count > 0)
            criteria.Add("OrderStatus", 查询操作符.包含某值, search.OrderStatuses.Distinct().ToList());

        if (search.ApprovalStatuses?.Count > 0)
            criteria.Add("ApprovalStatus", 查询操作符.包含某值, search.ApprovalStatuses.Distinct().ToList());

        if (!string.IsNullOrWhiteSpace(search.IncludeSkus))
        {
            var skuKeywords = search.IncludeSkus
                .Split([',', '\r', '\n', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (skuKeywords.Count > 0)
            {
                var allLines = await _lineRepository.GetAllAsync();
                var matchedOrderNos = allLines
                    .Where(l => !string.IsNullOrWhiteSpace(l.OrderNo)
                                && !string.IsNullOrWhiteSpace(l.Sku)
                                && skuKeywords.Any(k => l.Sku!.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .Select(l => l.OrderNo!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (matchedOrderNos.Count == 0)
                {
                    criteria.Add("Id", 查询操作符.等于, -1);
                }
                else
                {
                    criteria.Add("OrderNo", 查询操作符.包含某值, matchedOrderNos);
                }
            }
        }

        return criteria;
    }

    /// <summary>
    /// 中文注释：按订单号查询明细行。
    /// </summary>
    private async Task<List<SalesOrderLine>> GetLinesByOrderNoAsync(string? orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) return [];

        var lines = await _lineRepository.GetListAsync(new { OrderNo = orderNo });
        return [.. lines.OrderBy(x => x.LineNumber)];
    }


    /// <summary>
    /// 创建一张订单（含行）事务。
    /// </summary>
    private async Task<ServiceResult<int>> SaveAsCreateAsync(SalesOrderDetailDto request)
    {
        if (request?.Header == null)
            return ServiceResult<int>.Fail("订单头不能为空");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            //转换成单头实体和行实体
            var header = request.Header.Adapt<SalesOrderHeader>();
            var lines = (request.Lines ?? []).Adapt<List<SalesOrderLine>>();

            //校验订单行数据是否合法（如果有订单行的话）
            var validateRst = await ValidateBusinessRulesAsync(header, lines);
            if (!validateRst.Success)
            {
                _session.Transaction.Rollback();
                return ServiceResult<int>.Fail(validateRst.Message);
            }


            //转换成单头实体并补全默认值
            header.IsDeleted = false;
            header.IsLocked = false;
            header.OrderStatus = SalesOrderStatusEnum.草稿;
            header.ApprovalStatus = ApprovalStatusEnum.草稿;

            //获取一个新的SO单号（如果请求中没有提供的话）
            if (string.IsNullOrWhiteSpace(header.OrderNo))
            {
                var newOrderNo = await _documentNoService.GetNextDocumentNoAsync(new DocumentNoRequestDto { DocType = DocumentTypeStaticsEnum.SalesOrder });
                if (!newOrderNo.Success)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<int>.Fail($"获取新单号失败：{newOrderNo.Message}");
                }
                header.OrderNo = newOrderNo.Data.DocumentNo;
            }

            //新增到数据库并获取新生成的订单ID
            int newId = await _repository.AddAsync(header);
            //批量新增订单行

            if (lines.Count > 0)
            {
                int lineNumber = 0;
                foreach (var line in lines)
                {
                    line.LineNumber = lineNumber++;
                    line.SalesOrderId = newId;
                    line.OrderNo = header.OrderNo;
                    line.IsDeleted = false;
                }

                await _lineRepository.AddRangeAsync(lines);
                await CreateDemandByLineListAsync(lines);//同步创建需求行（如果有订单行的话）
            }

            _session.Transaction.Commit();
            return ServiceResult<int>.Ok(newId);
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
    private async Task<ServiceResult<bool>> SaveAsUpdateAsync(int orderId, SalesOrderDetailDto request)
    {
        if (request?.Header == null)
            return ServiceResult<bool>.Fail("订单头不能为空");
        if (request.Header.ApprovalStatus == ApprovalStatusEnum.已批准)
            return ServiceResult<bool>.Fail("已批准的订单不允许修改");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            //转换成实体
            var processedLines = (request.Lines ?? []).Adapt<List<SalesOrderLine>>();
            //校验订单行数据是否合法（如果有订单行的话）
            var checkrst = await CheckLineListCanBeDeleteOrUpdatedAsync(OrderTypesEnum.销售订单, processedLines.Select(l => l.Id));//检查订单行是否都允许被修改或删除（如果有订单行被修改或删除的话）

            //校验通过
            if (!checkrst.Success)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail(checkrst.Message);
            }
            //首先获取数据库中订单头信息，如果不存在则回滚并返回失败结果
            var existingHeader = await _repository.GetByIdAsync(orderId);
            if (existingHeader == null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail("订单不存在");
            }

            //把请求体里的信息覆盖到数据库中查询出来的订单头实体上（只覆盖可修改的字段），并设置订单ID，准备更新到数据库
            request.Header.Adapt(existingHeader);
            existingHeader.Id = orderId;

            //更新订单头信息
            await _repository.UpdateAsync(existingHeader);


            // 3. 处理删除行（如果有订单行被删除的话）
            if (request.DeletedLineIds?.Count > 0)
            {
                await _lineRepository.DeleteRangeAsync(request.DeletedLineIds, false);
                await _demandRepo.DeleteRangeBySourceTypeAndSourceIdListAsync(OrderTypesEnum.销售订单, request.DeletedLineIds);
            }

            //4.处理新增行和更新行
            var addLins = new List<SalesOrderLine>();
            var updateLines = new List<SalesOrderLine>();
            //按照Id是否大于0来区分新增行和更新行，并设置订单行的OrderNo为订单头的OrderNo，准备后续批量新增或批量更新到数据库
            foreach (var line in processedLines)
            {
                line.OrderNo = existingHeader.OrderNo;
                line.SalesOrderId = orderId;
                line.IsDeleted = false;
                if (line.Id <= 0)
                {
                    line.Id = 0;
                    addLins.Add(line);
                }
                else
                {
                    updateLines.Add(line);
                }
            }

            if (addLins.Count > 0)
            {
                await _lineRepository.AddRangeAsync(addLins);
                await CreateDemandByLineListAsync(addLins);//同步创建需求行（如果有订单行被新增的话）
            }

            //5.处理更新
            string outRst1 = "订单更新成功";
            if (updateLines.Count > 0)
            {
                var rst1 = await _lineRepository.UpdateRangeAsync(updateLines);
                var rst2 = await UpdateDemandAsync(updateLines); //同步更新需求行（如果有订单行被修改的话）
                if (rst1 != rst2) outRst1 += "需求行更新结果不一致，请核查订单行和需求行数据";
            }
            _session.Transaction.Commit();
            return ServiceResult<bool>.Ok(true, outRst1);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<bool>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>
    /// 私有辅助：同步创建需求行
    /// </summary>
    private async Task CreateDemandByLineListAsync(List<SalesOrderLine> lineList)
    {
        if (lineList == null || lineList.Count == 0) return;

        var existingDemands = await _demandRepo.GetListBySourceTypeAndSourceIdAsync(OrderTypesEnum.销售订单, [.. lineList.Select(l => l.Id)]);
        foreach (var demand in existingDemands)
        {
            if (demand.StatusFlag != IndependentDemandStatusEnum.草稿)
            {
                throw new Exception($"无法创建订单行[{demand.SourceLineId}]，因为已提交过需求并相关需求行已进入处理流程");
            }
        }

        List<IndependentDemand> entitys = [];
        foreach (var line in lineList)
        {
            var demand = new IndependentDemand
            {
                Id = 0,
                SourceType = OrderTypesEnum.销售订单,
                SourceLineId = line.Id,
                SourceNo = line.OrderNo ?? string.Empty,
                SalesOrderId = line.SalesOrderId,
                Sku = line.Sku,
                Description = line.DescriptionEn,
                ColorName = line.ColorNameEn,
                Features = line.FeaturesEn,
                IsDeleted = false,
                OriginalQty = line.QTY ?? 0,
                FilledQty = 0,
                ShippedQty = 0,
                // 中文注释：审批前仅生成“毛需求”记录，状态应保持草稿。
                StatusFlag = IndependentDemandStatusEnum.草稿,
                IsDecomposed = false,
                DecomposedTime = null,
                HopefulDate = line.ExpectedDeliveryDate
            };
            entitys.Add(demand);
        }
        await _demandRepo.AddRangeAsync(entitys);
    }

    /// <summary>
    /// 同步更新需求行,
    /// </summary>
    /// <param name="lineList"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<int> UpdateDemandAsync(List<SalesOrderLine> lineList)
    {
        // 1. 获取数据库中现有的需求行（根据来源单号/ID）
        var existingDemands = await _demandRepo.GetListBySourceTypeAndSourceIdAsync(OrderTypesEnum.销售订单, [.. lineList.Select(l => l.Id)]);

        // 2. 将订单行DTO转换为字典，方便内存比对
        var lineDict = lineList.ToDictionary(l => l.Id);

        foreach (var demand in existingDemands)
        {
            // 只有在 lineDict 中能找到，且 DemandId 匹配的才处理更新
            if (lineDict.TryGetValue(demand.SourceLineId, out var lineEntity))
            {
                // --- 业务守卫：检查该行是否允许更新 ---
                if (demand.FilledQty > 0 || demand.StatusFlag != IndependentDemandStatusEnum.草稿)
                {
                    throw new Exception($"无法更新订单行[{demand.SourceLineId}]，因为已提交过需求并相关需求行已进入处理流程或已有生产记录");
                }

                // --- 选择性更新字段 ---
                demand.OriginalQty = lineEntity.QTY ?? 0;
                demand.Sku = lineEntity.Sku;
                demand.HopefulDate = lineEntity.ExpectedDeliveryDate;
                demand.Remark = lineEntity.SubNote;
                // 如果是非标件，同步描述信息
                demand.Description = lineEntity.DescriptionEn;
                demand.ColorName = lineEntity.ColorNameEn;
                demand.Features = lineEntity.FeaturesEn;
            }
        }

        // 3. 调用仓储的批量更新（一次性发往数据库）
        return await _demandRepo.UpdateRangeAsync(existingDemands);
    }

    /// <summary>
    /// 确认需求行是否可以删除,
    /// </summary>
    /// <param name="sourceType"></param>
    /// <param name="sourceIds"></param>
    /// <returns></returns>
    public async Task<ServiceResult<bool>> CheckLineListCanBeDeleteOrUpdatedAsync(string sourceType, IEnumerable<int> sourceIds)
    {
        var rst = await _demandRepo.GetListBySourceTypeAndSourceIdAsync(sourceType, sourceIds);
        if (rst.Count > 0)
        {
            foreach (var demand in rst)
            {
                // 2. 核心红线校验
                if (demand.StatusFlag != IndependentDemandStatusEnum.草稿)
                    return ServiceResult<bool>.Fail($"行 {demand.SourceLineId} 已进入流程，禁止直接修改或删除。");

                if (demand.FilledQty > 0)
                    return ServiceResult<bool>.Fail($"行  {demand.SourceLineId}  已有生产记录数量，禁止直接修改或删除。");

                if (demand.ShippedQty > 0)
                    return ServiceResult<bool>.Fail($"行  {demand.SourceLineId} 已有发货记录，禁止直接修改或删除。");
            }
        }
        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>
    /// 中文注释：客户名称字典缓存构建。
    /// </summary>
    private async Task<Dictionary<int, string>> BuildCustomerNameMapAsync()
    {
        var customers = await _customerRepository.GetAllAsync();
        return customers
            .Where(x => x.Id > 0)
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First().CustomerName ?? g.First().CustomerCode ?? string.Empty);
    }

    /// <summary>
    /// 中文注释：实体映射到旧版订单 DTO。
    /// </summary>
    private static SalesOrderDto MapToSalesOrderDto(SalesOrderHeader header, Dictionary<int, string> customerNameMap)
    {
        int customerId = header.CustomerId ?? 0;
        DateTime orderDate = header.OrderDate ?? DateTime.Now;

        return new SalesOrderDto
        {
            Id = header.Id,
            OrderNo = header.OrderNo ?? string.Empty,
            CustomerId = customerId,
            CustomerName = customerNameMap.TryGetValue(customerId, out var name)
                ? name
                : (header.CustomerCode ?? string.Empty),
            OrderDate = orderDate,
            DeliveryDate = header.DeliveryDate,
            TotalAmount = header.TotalAmountLocalCurrency ?? header.TotalAmountOriginalCurrency ?? 0m,
            OrderStatus = header.OrderStatus,
            Remark = header.Remark ?? string.Empty,
            LineCount = header.LineCount ?? 0,
            CreatedTime = header.CreatedDate ?? orderDate,
            ModifiedTime = header.UpdatedDate ?? orderDate
        };
    }

    /// <summary>
    /// 中文注释：审批前业务规则校验。
    /// 规则：
    /// 1) 订单头关键字段必须存在。
    /// 2) 订单必须存在有效明细行。
    /// 3) 明细行 SKU/数量必须有效。
    /// </summary>
    private static Task<ServiceResult<bool>> ValidateBusinessRulesAsync(SalesOrderHeader order, List<SalesOrderLine> orderLines)
    {
        if (order == null)
            return Task.FromResult(ServiceResult<bool>.Fail("订单头不存在"));

        if (string.IsNullOrWhiteSpace(order.OrderNo))
            return Task.FromResult(ServiceResult<bool>.Fail("订单号不能为空"));

        if (string.IsNullOrWhiteSpace(order.CustomerCode) && !order.CustomerId.HasValue)
            return Task.FromResult(ServiceResult<bool>.Fail("订单缺少客户信息"));

        if (orderLines == null || orderLines.Count == 0)
            return Task.FromResult(ServiceResult<bool>.Fail("订单明细不能为空"));

        foreach (var line in orderLines)
        {
            // 中文注释：数量必须大于0。
            if ((line.QTY ?? 0) <= 0)
                return Task.FromResult(ServiceResult<bool>.Fail($"订单行[{line.LineNumber}] 数量无效"));

            //CustomerSku和Sku至少要有一个不为空，否则无法识别订单行的物料信息
            if (string.IsNullOrWhiteSpace(line.CustomerSku) && string.IsNullOrWhiteSpace(line.Sku))
                return Task.FromResult(ServiceResult<bool>.Fail($"订单行[{line.LineNumber}] SKU和客户SKU不能同时为空"));

            //以下是Sku为空时的特殊校验逻辑，适用于非标件场景
            if (!string.IsNullOrWhiteSpace(line.Sku))
                continue;

            // 中文注释：非标件支持——SKU为空时，要求至少有描述或规格信息，避免出现无标识需求。
            var isNonStandardValid = xNotNullOrWhiteSpace(line.DescriptionEn)
                                     || xNotNullOrWhiteSpace(line.FeaturesEn)
                                     || xNotNullOrWhiteSpace(line.ColorNameEn);

            if (!isNonStandardValid)
                return Task.FromResult(ServiceResult<bool>.Fail($"订单行[{line.LineNumber}] SKU为空且非标描述信息不完整"));
        }

        return Task.FromResult(ServiceResult<bool>.Ok(true));

        static bool xNotNullOrWhiteSpace(string? value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>
    /// 中文注释：按来源单号批量更新销售需求状态。
    /// </summary>
    private async Task<ServiceResult<bool>> UpdateDemandStatusBySourceNoAsync(string sourceType, string sourceNo, int statusFlag)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
            return ServiceResult<bool>.Fail("来源类型和来源单号不能为空");

        var demands = await _demandRepo.GetListBySourceTypeAndSourceNoAsync(sourceType, sourceNo);
        if (demands == null || demands.Count == 0)
            return ServiceResult<bool>.Fail("未找到对应的销售需求记录");

        foreach (var demand in demands)
        {
            demand.StatusFlag = statusFlag;
            demand.UpdatedDate = DateTime.Now;
        }

        await _demandRepo.UpdateRangeAsync(demands);
        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>
    /// 中文注释：已批准订单驳回前检查销售需求是否允许回退。
    /// 规则：存在执行态（非草稿/待处理）或已有填充/发货数量时，不允许驳回。
    /// </summary>
    private async Task<ServiceResult<bool>> CanRejectApprovedOrderByDemandStatusAsync(string sourceType, string sourceNo)
    {
        var demands = await _demandRepo.GetListBySourceTypeAndSourceNoAsync(sourceType, sourceNo);
        if (demands == null || demands.Count == 0)
            return ServiceResult<bool>.Ok(true);

        var hasExecuted = demands.Any(x => x.StatusFlag > 0);
        if (hasExecuted)
            return ServiceResult<bool>.Fail("订单中存在已执行需求，不允许驳回");

        var hasBusinessData = demands.Any(x => x.FilledQty > 0 || x.ShippedQty > 0);
        if (hasBusinessData)
            return ServiceResult<bool>.Fail("订单中存在已填充或已发货需求，不允许驳回");

        return ServiceResult<bool>.Ok(true);
    }



    #endregion
}
