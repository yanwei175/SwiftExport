using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Orders;

public class SalesOrderDetailService(ServiceBaseContext context,
    ISalesOrderHeadRepository salesOrderHeadRepo,
    ISalesOrderLineRepository salesOrderLineRepo,
    IDocumentNoService documentNoService) : ISalesOrderDetailService
{
    protected readonly DbSession _session = context.Session;
    private readonly ISalesOrderHeadRepository _salesOrderHeadRepo = salesOrderHeadRepo;
    private readonly ISalesOrderLineRepository _salesOrderLineRepo = salesOrderLineRepo;
    private readonly IDocumentNoService _docService = documentNoService;
    private ILogger? _loggerInstance;
    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());

    public async Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailByHeadIdAsync(Guid orderUid)
    {
        try
        {
            var header = await _salesOrderHeadRepo.GetByIdAsync(orderUid);
            if (header == null)
                return ServiceResult<SalesOrderDetailDto>.Fail("未找到销售订单");

            var lines = await _salesOrderLineRepo.GetLinesByOrderIdAsync(orderUid);

            return ServiceResult<SalesOrderDetailDto>.Ok(new SalesOrderDetailDto
            {
                Header = header.Adapt<SalesOrderHeaderDto>(),
                Lines = lines.Adapt<List<SalesOrderLineDto>>()
            });
        }
        catch (Exception ex) { return ServiceResult<SalesOrderDetailDto>.Fail(ex.Message); }
    }
    /// <summary>
    /// 获取销售订单下一单号。
    /// </summary>
    public async Task<ServiceResult<DocumentNoResponse>> GetNextSalesOrderNoAsync()
    {
        var request = new DocumentNoRequest
        {
            DocType = DocumentTypeStaticsEnum.SalesOrder
        };
        return await _docService.GetNextDocumentNoAsync(request);
    }
    public async Task<ServiceResult<PagedResponseBase<SalesProductDto>>> GetSalesProductPagedListAsync(SalesProductSearchRequest request)
    {
        try
        {
            request ??= new SalesProductSearchRequest();

            // 中文注释：服务层仅做请求参数标准化，具体 SQL 条件由仓储层直解析 SalesProductSearchRequest。
            request.CustomerCode = string.IsNullOrWhiteSpace(request.CustomerCode)
                ? null
                : request.CustomerCode.Trim();

            if (request.BatchCustomerSkuList != null)
            {
                request.BatchCustomerSkuList = request.BatchCustomerSkuList
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (request.BatchKeywords != null)
            {
                request.BatchKeywords = request.BatchKeywords
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            request.PageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
            request.PageSize = request.PageSize > 0 ? request.PageSize : 50;

            var rst = await _salesOrderLineRepo.GetSalesProductPagedListAsync(request);
            return ServiceResult<PagedResponseBase<SalesProductDto>>.Ok(new PagedResponseBase<SalesProductDto>
            {
                Items = rst.Items,
                TotalCount = rst.TotalCount,
                PageIndex = rst.PageIndex,
                PageSize = rst.PageSize
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResponseBase<SalesProductDto>>.Fail("查询失败: " + ex.Message);
        }

    }

    public async Task<ServiceResult<SalesOrderSaveDetailResponse>> SaveSalesOrderAsync(SalesOrderSaveDetailRequest request)
    {
        //如果订单头信息不存在，直接返回失败结果
        if (request?.Header == null)
            return ServiceResult<SalesOrderSaveDetailResponse>.Fail("订单头不能为空");
        //如果订单头 ID 大于 0，视为更新操作；否则视为创建操作
        if (request.Header.Uid != Guid.Empty)
        {
            return await SaveAsUpdateAsync(request);
        }
        //创建操作
        return await SaveAsCreateAsync(request);
    }

    /// <summary>创建一张订单（含行）事务。</summary>
    private async Task<ServiceResult<SalesOrderSaveDetailResponse>> SaveAsCreateAsync(SalesOrderSaveDetailRequest request)
    {
        throw new NotImplementedException();
        //if (request?.Header == null)
        //    return ServiceResult<SalesOrderSaveDetailResponse>.Fail("订单头不能为空");

        //_session.Transaction = _session.Connection.BeginTransaction();
        //try
        //{
        //    //转换成单头实体和行实体
        //    var header = request.Header.Adapt<SalesOrderHeader>();
        //    var lines = request.Items.Adapt<List<SalesOrderLine>>();

        //    //校验订单行数据是否合法（如果有订单行的话）
        //    var isValid = await ValidateSalesOrderBeforeSaveAsync(header, lines, true);
        //    if (!isValid.Success)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<SalesOrderSaveDetailResponse>.Fail(isValid.Message);
        //    }

        //    //转换成单头实体并补全默认值
        //    header.IsDeleted = false;
        //    header.OrderStatus = SalesOrderStatusEnum.未确认;
        //    header.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        //    header.ApprovalStatus = ApprovalStatusEnum.待审批;
        //    header.Uid = GuidSequentialGenerator.NewGuid();
        //    //获取一个新的SO单号（如果请求中没有提供的话）
        //    if (string.IsNullOrWhiteSpace(header.OrderNo))
        //    {
        //        var newOrderNo = await _docService.GetNextDocumentNoAsync(new DocumentNoRequestDto { DocType = DocumentTypeStaticsEnum.SalesOrder });
        //        if (!newOrderNo.Success)
        //        {
        //            _session.Transaction.Rollback();
        //            return ServiceResult<SalesOrderSaveDetailResponse>.Fail($"获取新单号失败：{newOrderNo.Message}");
        //        }
        //        header.OrderNo = newOrderNo.Data.DocumentNo;
        //    }

        //    //新增到数据库并获取新生成的订单Uid
        //    Guid newUid = await _salesOrderHeadRepo.AddAsync(header);
        //    //批量新增订单行

        //    if (lines.Count > 0)
        //    {
        //        int lineNumber = 0;
        //        foreach (var line in lines)
        //        {
        //            line.LineNumber = lineNumber++;
        //            line.SalesOrderId = header.Uid;
        //            line.OrderNo = header.OrderNo;
        //            line.IsNonStandard = true;
        //            line.IsDeleted = false;
        //        }

        //        await _salesOrderLineRepo.AddRangeAsync(lines);
        //        //await CreateDemandByLineListAsync(lines);//同步创建需求行（如果有订单行的话）
        //    }
        //    _session.Transaction.Commit();
        //    var newHeader = await _salesOrderHeadRepo.GetByIdAsync(newUid);
        //    var newLines = await _salesOrderLineRepo.GetLinesByOrderIdAsync(newUid);

        //    return ServiceResult<SalesOrderSaveDetailResponse>.Ok(new SalesOrderSaveDetailResponse { Header = newHeader.Adapt<SalesOrderHeaderDto>(), Items = newLines.Adapt<List<SalesOrderLineDto>>() });
        //}
        //catch
        //{
        //    _session.Transaction.Rollback();
        //    throw;
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
    }

    /// <summary>更新一张订单（含行）事务。 </summary>
    private async Task<ServiceResult<SalesOrderSaveDetailResponse>> SaveAsUpdateAsync(SalesOrderSaveDetailRequest request)
    {
        //if (request?.Header == null)
        //    return ServiceResult<SalesOrderSaveDetailResponse>.Fail("订单头不能为空");
        //if (request.Header.ConfirmationStatus == ConfirmationStatusEnum.已确认)
        //    return ServiceResult<SalesOrderSaveDetailResponse>.Fail("已确认的订单不允许修改");

        //_session.Transaction = _session.Connection.BeginTransaction();
        //try
        //{
        //    //转换成实体
        //    //首先获取数据库中订单头信息，如果不存在则回滚并返回失败结果
        //    var isHeadExists = await _salesOrderHeadRepo.ExistsIdAsync(request.Header.Uid);
        //    if (!isHeadExists)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<SalesOrderSaveDetailResponse>.Fail("订单不存在");
        //    }
        //    var header = request.Header.Adapt<SalesOrderHeader>();
        //    var lines = request.Items.Adapt<List<SalesOrderLine>>();
        //    //校验订单行数据是否合法（如果有订单行的话）
        //    var checkrst = await ValidateSalesOrderBeforeSaveAsync(header, lines, false);//检查订单行是否都允许被修改或删除（如果有订单行被修改或删除的话）

        //    //校验通过
        //    if (!checkrst.Success)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<SalesOrderSaveDetailResponse>.Fail(checkrst.Message);
        //    }

        //    //更新订单头信息
        //    await _salesOrderHeadRepo.UpdateAsync(header);

        //    // 3. 处理删除行（如果有订单行被删除的话）
        //    if (request.DeletedItemIds?.Count > 0)
        //    {
        //        await _salesOrderLineRepo.DeleteRangeAsync(request.DeletedItemIds, false);
        //    }

        //    //4.处理新增行和更新行
        //    var addLins = new List<SalesOrderLine>();
        //    var updateLines = new List<SalesOrderLine>();
        //    //按照Id是否大于0来区分新增行和更新行，并设置订单行的OrderNo为订单头的OrderNo，准备后续批量新增或批量更新到数据库
        //    int linNum = 1;
        //    foreach (var line in lines)
        //    {
        //        line.SalesOrderId = header.Uid;
        //        line.OrderNo = header.OrderNo;
        //        line.IsDeleted = false;
        //        line.LineNumber = linNum++;
        //        if (line.Uid == Guid.Empty)
        //        {
        //            await _salesOrderLineRepo.AddAsync(line);
        //        }
        //        else
        //        {
        //            await _salesOrderLineRepo.UpdateAsync(line);
        //        }
        //    }

        //    _session.Transaction.Commit();
        //    var newHeader = await _salesOrderHeadRepo.GetByIdAsync(header.Uid);
        //    var newLines = await _salesOrderLineRepo.GetLinesByOrderIdAsync(header.Uid);
        //    return ServiceResult<SalesOrderSaveDetailResponse>.Ok(new SalesOrderSaveDetailResponse
        //    {
        //        Header = newHeader.Adapt<SalesOrderHeaderDto>(),
        //        Items = newLines.Adapt<List<SalesOrderLineDto>>()
        //    });
        //}
        //catch (Exception ex)
        //{
        //    _session.Transaction.Rollback();
        //    return ServiceResult<SalesOrderSaveDetailResponse>.Fail(ex.Message);
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
        throw new NotImplementedException();
    }


    private static async Task<ServiceResult<bool>> ValidateSalesOrderBeforeSaveAsync(SalesOrderHeader order, List<SalesOrderLine> orderLines, bool isCreate)
    {

        if (order == null)
            return ServiceResult<bool>.Fail("订单头不存在");

        if (!isCreate)
        {
            if (string.IsNullOrWhiteSpace(order.OrderNo))
                return ServiceResult<bool>.Fail("更新订单时,订单号不能为空");
            if (order.Uid == Guid.Empty)
                return ServiceResult<bool>.Fail("更新订单时,UID不能为空");
        }

        if (order.CustomerId == Guid.Empty)
            return ServiceResult<bool>.Fail("订单缺少客户信息");

        if (orderLines == null || orderLines.Count == 0)
            return ServiceResult<bool>.Fail("订单明细不能为空");

        foreach (var line in orderLines)
        {
            //Sku不为空，否则无法识别订单行的物料信息
            if (string.IsNullOrWhiteSpace(line.Sku))
                return ServiceResult<bool>.Fail($"订单行[{line.LineNumber}] SKU不能为空");
            // 数量必须大于0。
            if (line.Qty <= 0)
                return ServiceResult<bool>.Fail($"订单行[{line.Sku}] 数量无效");
        }

        return ServiceResult<bool>.Ok(true);

    }
}
