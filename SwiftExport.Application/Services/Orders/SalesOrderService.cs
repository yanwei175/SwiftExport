using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
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
    IRepository<SalesOrderHeader> headerRepository,
    IRepository<SalesOrderLine> lineRepository,
    IRepository<Customer> customerRepository,
    ServiceBaseContext context)
    : BaseService<SalesOrderHeader, SalesOrderDto>(headerRepository, context), ISalesOrderService
{
    private readonly IRepository<SalesOrderLine> _lineRepository = lineRepository;
    private readonly IRepository<Customer> _customerRepository = customerRepository;

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
            Items = paged.Items.Adapt<IReadOnlyList<SalesOrderHeaderDto>>(),
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
                    item.OrderNo = EnsureOrderNo(item.OrderNo);
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
                    item.OrderNo = EnsureOrderNo(item.OrderNo);
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

                await _repository.DeleteRangeAsync(request.HeaderChanges.DeleteIdList.Distinct().ToList());
            }

            // 中文注释：处理当前选中订单的行批量变更。
            if (request.CurrentOrderId.HasValue && request.LineChanges != null)
            {
                var header = await _repository.GetByIdAsync(request.CurrentOrderId.Value);
                if (header == null)
                    return ServiceResult<bool>.Fail("当前订单不存在，无法保存明细行");

                string orderNo = EnsureOrderNo(header.OrderNo);

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
                    await _lineRepository.DeleteRangeAsync(request.LineChanges.DeleteIdList.Distinct().ToList());
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

    /// <summary>
    /// 中文注释：获取订单状态筛选字典。
    /// </summary>
    public Task<ServiceResult<List<SalesOrderStatusOptionDto>>> GetOrderStatusOptionsAsync()
    {
        var options = new List<SalesOrderStatusOptionDto>
        {
            new() { Name = "未发货", Value = SalesOrderStatus.未发货, Sort = 1 },
            new() { Name = "部分发货", Value = SalesOrderStatus.部分发货, Sort = 2 },
            new() { Name = "全部发完", Value = SalesOrderStatus.全部发完, Sort = 3 },
            new() { Name = "收到货物", Value = SalesOrderStatus.收到货物, Sort = 4 },
            new() { Name = "完成", Value = SalesOrderStatus.完成, Sort = 5 }
        };

        return Task.FromResult(ServiceResult<List<SalesOrderStatusOptionDto>>.Ok(options));
    }

    /// <summary>
    /// 中文注释：获取审批状态筛选字典。
    /// </summary>
    public Task<ServiceResult<List<SalesOrderStatusOptionDto>>> GetApprovalStatusOptionsAsync()
    {
        var options = new List<SalesOrderStatusOptionDto>
        {
            new() { Name = "草稿", Value = "Draft", Sort = 1 },
            new() { Name = "待审批", Value = "Pending", Sort = 2 },
            new() { Name = "已审批", Value = "Approved", Sort = 3 },
            new() { Name = "已驳回", Value = "Rejected", Sort = 4 }
        };

        return Task.FromResult(ServiceResult<List<SalesOrderStatusOptionDto>>.Ok(options));
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

        if (!string.IsNullOrWhiteSpace(search.OrderStatus))
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
            var header = request.Header.Adapt<SalesOrderHeader>();
            header.CreatedDate ??= DateTime.Now;
            header.IsDeleted = false;
            header.OrderNo = EnsureOrderNo(header.OrderNo);

            int newId = await _repository.AddAsync(header);

            var lines = (request.Lines ?? []).Adapt<List<SalesOrderLine>>();
            if (lines.Count > 0)
            {
                foreach (var line in lines)
                {
                    line.Id = 0;
                    line.OrderNo = header.OrderNo;
                    line.CreatedDate ??= DateTime.Now;
                    line.IsDeleted = false;
                }

                await _lineRepository.AddRangeAsync(lines);
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

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            //首先获取数据库中订单头信息，如果不存在则回滚并返回失败结果
            var existingHeader = await _repository.GetByIdAsync(orderId);
            if (existingHeader == null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<bool>.Fail("订单不存在");
            }
            //Dto转换成实体
            request.Header.Adapt(existingHeader);
            existingHeader.Id = orderId;
            existingHeader.UpdatedDate = DateTime.Now;
            existingHeader.OrderNo = EnsureOrderNo(existingHeader.OrderNo);
            //更新订单头信息
            await _repository.UpdateAsync(existingHeader);

            //删除原有订单行信息之后再新增订单行
            var oldLines = await GetLinesByOrderNoAsync(existingHeader.OrderNo);
            if (oldLines.Count > 0)
            {
                await _lineRepository.DeleteRangeAsync(oldLines.Select(x => x.Id));
            }
            //批量新增订单行
            var newLines = (request.Lines ?? []).Adapt<List<SalesOrderLine>>();
            if (newLines.Count > 0)
            {
                foreach (var line in newLines)
                {
                    line.Id = 0;
                    line.OrderNo = existingHeader.OrderNo;
                    line.CreatedDate ??= DateTime.Now;
                    line.IsDeleted = false;
                }

                await _lineRepository.AddRangeAsync(newLines);
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

    /// <summary>
    /// 中文注释：生成或返回订单号。
    /// </summary>
    private static string EnsureOrderNo(string? orderNo)
    {
        if (!string.IsNullOrWhiteSpace(orderNo)) return orderNo;
        return $"SO{DateTime.Now:yyyyMMddHHmmssfff}";
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
            .ToDictionary(g => g.Key, g => g.First().Name ?? g.First().CustomerCode ?? string.Empty);
    }

    /// <summary>
    /// 中文注释：实体映射到旧版订单 DTO。
    /// </summary>
    private static SalesOrderDto MapToSalesOrderDto(SalesOrderHeader header, IReadOnlyDictionary<int, string> customerNameMap)
    {
        int customerId = header.CustomerId ?? 0;
        DateTime orderDate = header.OrderDate ?? header.CreatedDate ?? DateTime.Now;

        return new SalesOrderDto
        {
            Id = header.Id,
            OrderNo = header.OrderNo ?? string.Empty,
            CustomerId = customerId,
            CustomerName = customerNameMap.TryGetValue(customerId, out var name)
                ? name
                : (header.CustomerCode ?? string.Empty),
            OrderDate = orderDate,
            DeliveryDate = header.DeliveryDate ?? orderDate,
            TotalAmount = header.TotalAmountLocalCurrency ?? header.TotalAmountOriginalCurrency ?? 0m,
            OrderStatus = header.OrderStatus ?? string.Empty,
            Remark = header.Remark ?? string.Empty,
            LineCount = header.LineCount ?? 0,
            CreatedTime = header.CreatedDate ?? orderDate,
            ModifiedTime = header.UpdatedDate ?? orderDate
        };
    }

    #endregion
}
