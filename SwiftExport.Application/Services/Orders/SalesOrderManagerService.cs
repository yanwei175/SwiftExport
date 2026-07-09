using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Orders;

public class SalesOrderManagerService(ServiceBaseContext context,
    ISalesOrderHeadRepository salesOrderHeadRepo,
    ISalesOrderLineRepository salesOrderLineRepo,
    IIndependentDemandRepository independentDemandRepo,
    IDocumentNoService documentNoService,
    IDependentDemandRepository dependentDemandRepository) : ISalesOrderManagerService
{
    protected readonly DbSession _session = context.Session;
    private readonly ISalesOrderHeadRepository _salesOrderHeadRepo = salesOrderHeadRepo;
    private readonly ISalesOrderLineRepository _salesOrderLineRepo = salesOrderLineRepo;
    private readonly IIndependentDemandRepository _independentDemandRepo = independentDemandRepo;
    private readonly IDependentDemandRepository _dependentDemandRepo = dependentDemandRepository;
    private readonly IDocumentNoService _docNoService = documentNoService;
    private ILogger? _loggerInstance;
    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());

    /// <summary>
    /// 中文注释：分页查询订单头列表（用于主表）。
    /// </summary>
    public async Task<ServiceResult<PagedResponseBase<SalesOrderHeaderManagerDto>>> GetSalesOrderHeaderManagerPagedListAsync(SalesOrderManagerSearchRequest req)
    {
        try
        {
            var rst = await _salesOrderHeadRepo.GetSalesOrderHeaderManagerPagedListAsync(req);
            if (rst != null)
            {
                return ServiceResult<PagedResponseBase<SalesOrderHeaderManagerDto>>.Ok(rst);
            }
            else
            {
                return ServiceResult<PagedResponseBase<SalesOrderHeaderManagerDto>>.Fail("查询订单列表失败");
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResponseBase<SalesOrderHeaderManagerDto>>.Fail($"查询订单列表失败：{ex.Message}");
        }

    }

    public async Task<ServiceResult<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerByIdAsync(Guid orderId)
    {
        try
        {
            var rst = await _salesOrderHeadRepo.GetSalesOrderHeaderManagerByIdAsync(orderId);
            if (rst != null)
            {
                return ServiceResult<SalesOrderHeaderManagerDto>.Ok(rst);
            }
            else
            {
                return ServiceResult<SalesOrderHeaderManagerDto>.Fail("查询订单列表失败");
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<SalesOrderHeaderManagerDto>.Fail($"查询订单列表失败：{ex.Message}");
        }
    }


    /// <summary>
    /// 中文注释：根据订单 ID 获取订单行列表。
    /// </summary>
    public async Task<ServiceResult<List<SalesOrderLineManagerDto>>> GetSalesOrderLineManagerAsync(Guid orderId)
    {
        var lines = await _salesOrderLineRepo.GetSalesOrderLineManagerAsync(orderId);
        return ServiceResult<List<SalesOrderLineManagerDto>>.Ok(lines.Adapt<List<SalesOrderLineManagerDto>>());
    }
    public async Task<ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>> BatchUpdateHeadManagerAsync(BatchSalesOrderHeadManagerUpdateRequestDto request)
    {
        if (request.UpdateList == null || request.UpdateList.Count == 0)
        {
            return ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>.Fail("没有订单头需要更新");
        }

        _session.Transaction = _session.Connection.BeginTransaction();

        try
        {
            var sussUids = new List<Guid>();
            foreach (var item in request.UpdateList)
            {
                var head = await _salesOrderHeadRepo.GetByIdAsync(item.Uid, true);
                if (head == null)
                {
                    return ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>.Fail($"订单头 {item.InvoiceNo} 不存在");
                }
                // 这里只更新订单头允许更新的，其他字段不允许修改
                head.Remark = item.Remark;
                head.DeliveryDate = item.DeliveryDate;
                head.CustomerPoNo = item.CustomerPoNo;
                head.InvoiceNo = item.InvoiceNo;
                head.ReplyDate = item.ReplyDate;
                head.SoFolder = item.SoFolder;
                var rst = await _salesOrderHeadRepo.UpdateAsync(head);
                sussUids.Add(rst.Uid);
            }

            if (sussUids.Count == 0)
            {
                _session.Transaction.Rollback();
                return ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>.Fail("没有订单头被更新");
            }

            var respo = await _salesOrderHeadRepo.GetSalesOrderHeaderManagerListByIdListAsync(sussUids);

            _session.Transaction.Commit();


            return ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>.Ok(new BatchSalesOrderHeadManagerUpdateResultDto
            {
                Items = respo.Count > 0 ? respo : []
            });
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>.Fail($"批量更新订单头失败：{ex.Message}");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    public async Task<ServiceResult<bool>> DeleteSalesOrderAsync(Guid orderId, int version)
    {
        try
        {
            var rst = await _salesOrderHeadRepo.DeleteSalesOrderAsync(orderId, version);
            return rst ? ServiceResult<bool>.Ok(true) : ServiceResult<bool>.Fail("删除订单失败");
        }
        catch (Exception ex) { return ServiceResult<bool>.Fail($"异常：{ex.Message}"); }

    }


    /// <summary>
    /// 确认销售订单。
    /// </summary>
    /// <param name="request">确认请求</param>
    /// <returns>操作结果</returns>
    public async Task<ServiceResult<ConfirmationItemResponse>> ConfirmSalesOrderAsync(ConfirmationItemRequest request)
    {
        throw new NotImplementedException();
        //_session.Transaction = _session.Connection.BeginTransaction();
        //try
        //{
        //    // 1. 获取并锁定数据
        //    var order = await _salesOrderHeadRepo.GetByIdAsync(request.Uid, true);
        //    if (order == null)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<ConfirmationItemResponse>.Fail("订单不存在");
        //    }

        //    // 2. 只能是未确认的订单才可以确认，避免重复.
        //    if (order.ConfirmationStatus != ConfirmationStatusEnum.待确认)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<ConfirmationItemResponse>.Fail("订单状态不合法，只有未确认状态的订单才能审批");
        //    }

        //    // 确认前查看下是否有毛需求行，如果有毛需求行则判断下毛需求是否是确认状态,如果是确认状态就报错出去,避免审批通过后毛需求行被执行拆解了。
        //    // 如果存在毛需求,并且没有进入下一步,就先删除毛需求,重新建立毛需求
        //    var independentDemands = await _independentDemandRepo.GetListBySourceTypeAndSourceUidAsync(OrderTypesEnum.销售订单, order.Uid);
        //    if (independentDemands.Count > 0)
        //    {
        //        if (independentDemands.Any(d => d.ConfirmationStatus == ConfirmationStatusEnum.已确认))
        //        {
        //            _session.Transaction.Rollback();
        //            return ServiceResult<ConfirmationItemResponse>.Fail($"{order.OrderNo} 存在已分解的需求，无法重新确认订单");
        //        }
        //        await _independentDemandRepo.DeleteRangeAsync(independentDemands.Select(d => d.Uid), false);
        //    }

        //    var orderLines = await _salesOrderLineRepo.GetLinesByOrderIdAsync(order.Uid);
        //    if (orderLines.Count == 0)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<ConfirmationItemResponse>.Fail($"{order.OrderNo} 订单没有明细行，无法确认");
        //    }

        //    var newIndepends = new List<IndependentDemand>();
        //    foreach (var it in orderLines)
        //    {
        //        var demand = new IndependentDemand
        //        {
        //            OrderStatus = IndependentDemandStatusEnum.未确认,
        //            ConfirmationStatus = ConfirmationStatusEnum.待确认,
        //            IsNonStandard = it.IsNonStandard,
        //            SourceType = OrderTypesEnum.销售订单,
        //            SourceUid = order.Uid,
        //            SourceNo = order.OrderNo,
        //            SourceLineId = it.Uid,
        //            SourceSku = it.CustomerSku,
        //            Sku = it.Sku,
        //            OriginalQty = it.Qty,
        //            FilledQty = 0,
        //            ShippedQty = 0,
        //            IsDecomposed = false,
        //            Description = it.DescriptionEn,
        //            ColorName = it.ColorNameEn,
        //            Features = it.FeaturesEn,
        //            HopefulDate = order.DeliveryDate ?? DateTime.Now.AddDays(60) // 默认交期为订单交期+60天
        //        };
        //        newIndepends.Add(demand);
        //    }

        //    await _independentDemandRepo.AddRangeAsync(newIndepends);


        //    // 4. 更新订单确认状态。
        //    order.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        //    order.OrderStatus = SalesOrderStatusEnum.已确认;
        //    var newHead = await _salesOrderHeadRepo.UpdateAsync(order);

        //    _session.Transaction.Commit();

        //    var response = new ConfirmationItemResponse
        //    {
        //        Uid = newHead.Uid,
        //        Success = true,
        //        Message = "订单确认成功",
        //        NewStatus = newHead.ConfirmationStatus,
        //        Version = newHead.Version,
        //        ConfirmedAt = newHead.UpdatedDate,
        //        ConfirmedBy = newHead.UpdatedBy
        //    };
        //    return ServiceResult<ConfirmationItemResponse>.Ok(response);
        //}
        //catch (Exception ex)
        //{
        //    _session.Transaction.Rollback();
        //    return ServiceResult<ConfirmationItemResponse>.Fail("确认订单失败" + ex.Message);
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
    }

    /// <summary>
    /// 取消确认销售订单
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<ServiceResult<ConfirmationItemResponse>> CancelConfirmSalesOrderAsync(ConfirmationItemRequest request)
    {
        throw new NotImplementedException();
        //_session.Transaction = _session.Connection.BeginTransaction();
        //try
        //{
        //    var order = await _salesOrderHeadRepo.GetByIdAsync(request.Uid);
        //    if (order == null)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<ConfirmationItemResponse>.Fail("订单不存在");
        //    }
        //    if (order.ConfirmationStatus == ConfirmationStatusEnum.待确认)
        //    {
        //        _session.Transaction.Rollback();
        //        return ServiceResult<ConfirmationItemResponse>.Fail($"订单 {order.OrderNo} 待确认状态下无需取消确认");
        //    }
        //    //查看是否已经有毛需求,并且毛需的确认状态是否是已确认,是已确认就查询净需求的状态是否已确认,如果净需求也确认了就报错,如果没有确认就删除毛需求,回退订单状态,重新建立毛需求
        //    var independs = await _independentDemandRepo.GetListBySourceTypeAndSourceUidAsync(OrderTypesEnum.销售订单, order.Uid);
        //    if (independs.Count > 0)
        //    {
        //        if (independs.Any(x => x.ConfirmationStatus == ConfirmationStatusEnum.已确认))
        //        {
        //            var dependents = await _dependentDemandRepo.GetListBySourceTypeAndSourceUidAsync(OrderTypesEnum.销售订单, order.Uid);
        //            if (dependents.Count > 0 && dependents.Any(x => x.ConfirmationStatus == ConfirmationStatusEnum.已确认))
        //            {
        //                _session.Transaction.Rollback();
        //                return ServiceResult<ConfirmationItemResponse>.Fail($"订单 {order.OrderNo} 的需求已创建并且已分解成净需求,请先去取消净需求!");
        //            }
        //            else
        //            {
        //                //净需求没有进入到下一步,直接删除净需求和毛需求
        //                await _independentDemandRepo.DeleteRangeAsync(independs.Select(x => x.Uid), false);
        //                await _dependentDemandRepo.DeleteRangeAsync(dependents.Select(x => x.Uid), false);
        //            }
        //        }
        //        else
        //        {
        //            //毛需求是未确认状态,直接删除毛需求
        //            await _independentDemandRepo.DeleteRangeAsync(independs.Select(x => x.Uid), false);
        //        }
        //    }
        //    //重置销售订单状态
        //    order.ConfirmationStatus = ConfirmationStatusEnum.待确认;
        //    order.OrderStatus = SalesOrderStatusEnum.未确认;

        //    var newHead = await _salesOrderHeadRepo.UpdateAsync(order);
        //    _session.Transaction.Commit();
        //    var response = new ConfirmationItemResponse
        //    {
        //        Uid = newHead.Uid,
        //        Success = true,
        //        Message = "成功",
        //        NewStatus = newHead.ConfirmationStatus,
        //        Version = newHead.Version,
        //        ConfirmedAt = newHead.UpdatedDate,
        //        ConfirmedBy = newHead.UpdatedBy
        //    };

        //    return ServiceResult<ConfirmationItemResponse>.Ok(response);
        //}
        //catch (Exception ex)
        //{
        //    _session.Transaction.Rollback();
        //    return ServiceResult<ConfirmationItemResponse>.Fail($"取消确认失败：{ex.Message}");
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
    }

    /// <summary>
    /// 批量确认销售订单
    /// </summary>
    /// <param name="request">批量确认请求</param>
    /// <returns>操作结果</returns>
    public async Task<ServiceResult<BatchConfirmationResponse>> BatchConfirmSalesOrdersAsync(BatchConfirmationRequest request)
    {
        try
        {
            var batchresponse = new BatchConfirmationResponse();
            foreach (var req in request.Items)
            {
                var confirmResult = await ConfirmSalesOrderAsync(req);
                if (confirmResult.Success)
                {
                    batchresponse.Items.Add(confirmResult.Data);
                }
                else
                {
                    batchresponse.Items.Add(confirmResult.Data);
                }
            }
            return ServiceResult<BatchConfirmationResponse>.Ok(batchresponse);
        }
        catch (Exception ex)
        {
            return ServiceResult<BatchConfirmationResponse>.Fail($"批量确认订单碰到错误：{ex.Message}");
        }
    }


}
