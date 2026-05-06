using Dapper;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders
{
    /// <summary>
    /// 采购入库应用服务。
    /// </summary>
    public class ReceiveService(
        IPurchaseReceiveHeadRepository receiveHeadRepository,
        IPurchaseReceiveLineRepository receiveLineRepository,
        IPurchaseOrderLineRepository purchaseOrderLineRepository,
        IDependentDemandRepository requirementPoolRepository,
        IIndependentDemandRepository salesDemandRepository,
        IInventoryRepository inventoryRepository,
        IDocumentNoService documentNoService,
        ServiceBaseContext context)
        : IReceiptService
    {
        private readonly IPurchaseReceiveHeadRepository _receiveHeadRepository = receiveHeadRepository;
        private readonly IPurchaseReceiveLineRepository _receiveLineRepository = receiveLineRepository;
        private readonly IPurchaseOrderLineRepository _purchaseOrderLineRepository = purchaseOrderLineRepository;
        private readonly IDependentDemandRepository _requirementPoolRepository = requirementPoolRepository;
        private readonly IIndependentDemandRepository _salesDemandRepository = salesDemandRepository;
        private readonly IInventoryRepository _inventoryRepository = inventoryRepository;
        private readonly IDocumentNoService _documentNoService = documentNoService;
        private readonly SwiftExport.Infrastructure.Data.DbSession _session = context.Session;

        /// <summary>
        /// 执行采购入库：
        /// 1) 生成入库头/行；
        /// 2) 回写采购行 RecQty；
        /// 3) 回写需求池 ReceivedQty；
        /// 4) 回写销售需求 FilledQty；
        /// 5) 更新虚拟库库存（WhseCode=V_WH，LocationCode=SupplierCode）。
        /// 全流程在同一事务中执行。
        /// </summary>
        public async Task<ServiceResult<string>> ExecutePurchaseReceiveAsync(PurchaseReceiveExecuteRequestDto request)
        {
            if (request == null)
                return ServiceResult<string>.Fail("入库请求不能为空");

            if (string.IsNullOrWhiteSpace(request.SupplierCode))
                return ServiceResult<string>.Fail("供应商不能为空");

            if (request.Lines == null || request.Lines.Count == 0)
                return ServiceResult<string>.Fail("入库行不能为空");

            if (request.Lines.Any(x => string.IsNullOrWhiteSpace(x.Sku) || x.Qty <= 0))
                return ServiceResult<string>.Fail("存在无效入库行（SKU 为空或数量<=0）");

            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                // 中文注释：生成入库单号，沿用单号服务，类型暂使用 DependentDemand 避免改动字典配置。
                var receiveNoResult = await _documentNoService.GetNextDocumentNoAsync(new DocumentNoRequestDto
                {
                    DocType = DocumentTypeStaticsEnum.InboundOrder,
                    BusinessDate = request.ReceiveDate ?? DateTime.Now,
                    SerialWidth = 4,
                    Operator = request.Handler
                });

                if (!receiveNoResult.Success || receiveNoResult.Data == null)
                    throw new Exception("生成入库单号失败：" + receiveNoResult.Message);

                var receiveNo = receiveNoResult.Data.DocumentNo;

                // 1) 生成入库头
                var head = new PurchaseReceiveHead
                {
                    ReceiveNo = receiveNo,
                    PoNo = request.PoNo,
                    OrderNo = request.OrderNo,
                    SupplierCode = request.SupplierCode,
                    ReceiveDate = request.ReceiveDate ?? DateTime.Now,
                    Handler = request.Handler,
                    Remark = request.Remark,
                    CreatedDate = DateTime.Now,
                    IsDeleted = false
                };
                await _receiveHeadRepository.AddAsync(head);

                // 2) 生成入库行
                var lineEntities = request.Lines.Select(x => new PurchaseReceiveLine
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

                await _receiveLineRepository.AddRangeAsync(lineEntities);

                // 3) 回写采购单行 RecQty（按 PoLineId 聚合）
                var poUpdates = request.Lines
                    .Where(x => x.PoLineId.HasValue)
                    .GroupBy(x => x.PoLineId!.Value)
                    .Select(g => new { PoLineId = g.Key, Qty = g.Sum(x => x.Qty) })
                    .ToList();

                foreach (var po in poUpdates)
                {
                    if (po.PoLineId > int.MaxValue)
                    {
                        continue;
                    }

                    var poLine = await _purchaseOrderLineRepository.GetByIdAsync((int)po.PoLineId);
                    if (poLine == null) continue;

                    poLine.RecQty = (poLine.RecQty <= 0 ? 0 : poLine.RecQty) + po.Qty;
                    poLine.LastRecDate = request.ReceiveDate ?? DateTime.Now;
                    poLine.ChangeTime = DateTime.Now;
                    await _purchaseOrderLineRepository.UpdateAsync(poLine);
                }

                // 4) 回写需求池 ReceivedQty（按 DemandId 聚合）
                var soUpdates = request.Lines
                    .Where(x => x.SoLineId.HasValue)
                    .GroupBy(x => x.SoLineId!.Value)
                    .Select(g => new { SoLineId = g.Key, Qty = g.Sum(x => x.Qty) })
                    .ToList();

                foreach (var so in soUpdates)
                {
                    // 中文注释：智能回写——同一销售行可能拆成多条需求池记录，按未入库缺口比例分摊本次入库数量。
                    var rpList = (await _requirementPoolRepository.GetListBySourceSoLineIdAsync(OrderTypesEnum.销售订单, so.SoLineId)).ToList();
                    if (rpList.Count == 0) continue;

                    var pendingRows = rpList
                        .Select(x => new
                        {
                            Row = x,
                            Gap = Math.Max(x.NetQty - x.ReceivedQty, 0m)
                        })
                        .Where(x => x.Gap > 0)
                        .ToList();

                    if (pendingRows.Count == 0) continue;

                    var totalGap = pendingRows.Sum(x => x.Gap);
                    var leftQty = so.Qty;

                    for (int i = 0; i < pendingRows.Count; i++)
                    {
                        var item = pendingRows[i];

                        decimal allocateQty;
                        if (i == pendingRows.Count - 1)
                        {
                            allocateQty = Math.Min(item.Gap, leftQty);
                        }
                        else
                        {
                            var ratioQty = totalGap <= 0 ? 0 : Math.Round(so.Qty * item.Gap / totalGap, 4, MidpointRounding.AwayFromZero);
                            allocateQty = Math.Min(item.Gap, ratioQty);
                            if (allocateQty > leftQty) allocateQty = leftQty;
                        }

                        if (allocateQty <= 0) continue;

                        item.Row.ReceivedQty = Math.Min(item.Row.NetQty, item.Row.ReceivedQty + allocateQty);
                        item.Row.ChangeTime = DateTime.Now;

                        if (item.Row.ReceivedQty >= item.Row.NetQty)
                        {
                            item.Row.StatusFlag = IndependentDemandStatusEnum.已到货;
                        }

                        await _requirementPoolRepository.UpdateAsync(item.Row);
                        leftQty -= allocateQty;

                        if (leftQty <= 0) break;
                    }
                }

                // 4.1) 回写 IndependentDemand FilledQty（按来源类型 + DemandId 聚合），避免来源类型硬编码。
                var demandIncreaseBySourceType = request.Lines
                    .Where(x => x.SoLineId.HasValue && x.SoLineId > 0 && x.SoLineId <= int.MaxValue && x.Qty > 0)
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.SourceType) ? OrderTypesEnum.销售订单 : x.SourceType.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => (IDictionary<int, decimal>)g
                            .GroupBy(x => (int)x.SoLineId!.Value)
                            .ToDictionary(x => x.Key, x => x.Sum(y => y.Qty)),
                        StringComparer.OrdinalIgnoreCase);

                if (demandIncreaseBySourceType.Count > 0)
                {
                    await _salesDemandRepository.批量更新完工数量Async(demandIncreaseBySourceType);
                }

                // 5) 更新库存：WhseCode='V_WH'、LocationCode=SupplierCode、Sku
                var inventoryUpdates = request.Lines
                    .GroupBy(x => new { SupplierCode = request.SupplierCode.Trim(), Sku = x.Sku.Trim(), x.Unit })
                    .Select(g => new
                    {
                        g.Key.SupplierCode,
                        g.Key.Sku,
                        g.Key.Unit,
                        Qty = g.Sum(x => x.Qty)
                    })
                    .ToList();

                foreach (var inv in inventoryUpdates)
                {
                    var stock = await _inventoryRepository.GetByWhseLocationSkuAsync("V_WH", inv.SupplierCode, inv.Sku);
                    if (stock == null)
                    {
                        stock = new Inventory
                        {
                            WhseCode = "V_WH",
                            LocationCode = inv.SupplierCode,
                            Sku = inv.Sku,
                            Unit = inv.Unit,
                            OnhandQty = inv.Qty,
                            CreatedDate = DateTime.Now,
                            IsDeleted = false
                        };
                        await _inventoryRepository.AddAsync(stock);
                    }
                    else
                    {
                        stock.OnhandQty += inv.Qty;
                        stock.UpdatedDate = DateTime.Now;
                        await _inventoryRepository.UpdateAsync(stock);
                    }
                }

                // 6) 库存流水（可选）：若存在 InventoryTransaction 表则写入一笔 Receive 流水。
                // 中文注释：当前采用 try-catch 包裹，不影响主事务核心链路。
                try
                {
                    const string txSql = @"IF OBJECT_ID('dbo.InventoryTransaction','U') IS NOT NULL
                                                    BEGIN
                                                        INSERT INTO [dbo].[InventoryTransaction]
                                                        ([WhseCode],[LocationCode],[Sku],[Qty],[BizType],[BizNo],[CreatedDate])
                                                        VALUES (@WhseCode,@LocationCode,@Sku,@Qty,@BizType,@BizNo,@CreatedDate)
                                                    END";

                    foreach (var inv in inventoryUpdates)
                    {
                        await _session.Connection.ExecuteAsync(
                            txSql,
                            new
                            {
                                WhseCode = "V_WH",
                                LocationCode = inv.SupplierCode,
                                inv.Sku,
                                inv.Qty,
                                BizType = "Receive",
                                BizNo = receiveNo,
                                CreatedDate = DateTime.Now
                            },
                            transaction: _session.Transaction);
                    }
                }
                catch
                {
                    // 中文注释：库存流水失败不阻断主流程。
                }

                _session.Transaction.Commit();
                return ServiceResult<string>.Ok(receiveNo);
            }
            catch (Exception ex)
            {
                _session.Transaction.Rollback();
                return ServiceResult<string>.Fail("执行采购入库失败：" + ex.Message);
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }
    }
}
