using Dapper;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Linq;

namespace SwiftExport.Application.Services.Orders
{
    /// <summary>
    /// 采购入库应用服务。
    /// </summary>
    public class ReceiveService(
        IReceiveHeadRepository receiveHeadRepository,
        IReceiveLineRepository receiveLineRepository,
        IPurchaseOrderRepository purchaseOrderRepository,
        IPurchaseOrderLineRepository purchaseOrderLineRepository,
        IDependentDemandRepository dependRepo,
        IIndependentDemandRepository salesDemandRepository,
        IInventoryRepository inventoryRepository,
        IInventoryTransactionRepository inventoryTransactionRepository,
        IDocumentNoService documentNoService,
        ServiceBaseContext context)
        : IReceiptService
    {
        private readonly IReceiveHeadRepository _receiveHeadRepository = receiveHeadRepository;
        private readonly IReceiveLineRepository _receiveLineRepository = receiveLineRepository;
        private readonly IPurchaseOrderRepository _purchaseOrderRepository = purchaseOrderRepository;
        private readonly IPurchaseOrderLineRepository _purchaseOrderLineRepository = purchaseOrderLineRepository;
        private readonly IDependentDemandRepository _dependRepo = dependRepo;
        private readonly IIndependentDemandRepository _salesDemandRepository = salesDemandRepository;
        private readonly IInventoryRepository _inventoryRepository = inventoryRepository;
        private readonly IInventoryTransactionRepository _inventoryTransactionRepository = inventoryTransactionRepository;
        private readonly IDocumentNoService _documentNoService = documentNoService;
        private readonly DbSession _session = context.Session;

        /// <summary>
        /// 按供应商与物料类型获取待入库候选行。
        /// 默认物料类型为客供件。
        /// </summary>
        public async Task<ServiceResult<IReadOnlyList<AwatingInboundDto>>> GetAwatingInboundLinesAsync(string supplierCode, string? materialType)
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                return ServiceResult<IReadOnlyList<AwatingInboundDto>>.Fail("供应商不能为空");

            var material = string.IsNullOrWhiteSpace(materialType)
                ? MaterialTypesEnum.客供件
                : materialType.Trim();

            var heads = await _purchaseOrderRepository.GetListAsync(new { SupplierCode = supplierCode.Trim() });
            if (heads == null || heads.Count == 0)
                return ServiceResult<IReadOnlyList<AwatingInboundDto>>.Ok(new List<AwatingInboundDto>());

            var headByOrderNo = heads
                .Where(x => !string.IsNullOrWhiteSpace(x.OrderNo))
                .GroupBy(x => x.OrderNo)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var lines = await _purchaseOrderLineRepository.GetListAsync(new { MaterialType = material });
            var result = lines
                .Where(x => !x.IsDeleted
                            && !string.IsNullOrWhiteSpace(x.OrderNo)
                            && headByOrderNo.ContainsKey(x.OrderNo)
                            && x.QTY > (x.RecQty <= 0 ? 0 : x.RecQty))
                .Select(x =>
                {
                    var head = headByOrderNo[x.OrderNo];
                    var can = x.QTY - (x.RecQty <= 0 ? 0 : x.RecQty);
                    return new AwatingInboundDto
                    {
                        PoNo = string.IsNullOrWhiteSpace(head.BoNo) ? head.OrderNo : head.BoNo,
                        PoLineId = x.Id,
                        Sku = x.Sku,
                        PoQty = x.QTY,
                        RecQty = x.RecQty,
                        ReadyQty = x.ReadyQty,
                        CanReceiveQty = can,
                        Unit = string.Empty,
                        MaterialType = x.MaterialType ?? string.Empty,
                        SupplierCode = head.SupplierCode ?? string.Empty
                    };
                })
                .OrderBy(x => x.PoNo)
                .ThenBy(x => x.PoLineId)
                .ToList();

            return ServiceResult<IReadOnlyList<AwatingInboundDto>>.Ok(result);
        }

        /// <summary>
        /// 兼容旧入口：直接执行采购入库。
        /// 新设计下先保存入库单（草稿），再调用过账服务执行库存/需求/采购行计算。
        /// </summary>
        public async Task<ServiceResult<string>> ExecutePurchaseReceiveAsync(PurchaseReceiveExecuteRequestDto request)
        {
            if (request == null)
                return ServiceResult<string>.Fail("入库请求不能为空");

            var saveReq = new SaveDocumentRequestBase<ReceiveHeadDto, ReceiveLineDto>
            {
                Header = new ReceiveHeadDto
                {
                    SupplierCode = request.SupplierCode,
                    ReceiveDate = request.ReceiveDate,
                    Handler = request.Handler,
                    Remark = request.Remark,
                    RelatedPoNos = string.Join(",", request.Lines
                        .Select(x => x.PoNo)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                },
                AddLines = request.Lines.Select(x => new ReceiveLineDto
                {
                    ReceiveNo = string.Empty,
                    Sku = x.Sku,
                    Qty = x.Qty,
                    Unit = x.Unit,
                    PoLineId = x.PoLineId,
                    PoNo = x.PoNo
                }).ToList()
            };

            var saveRst = await SaveDetailAsync(saveReq);
            if (!saveRst.Success)
                return ServiceResult<string>.Fail(saveRst.Message);

            var postRst = await PostReceiveDetialAsync(saveRst.Data.Id);
            if (!postRst.Success)
                return ServiceResult<string>.Fail(postRst.Message);

            return ServiceResult<string>.Ok(postRst.Data.ReceiveNo);
        }


        /// <summary>
        /// 执行采购入库：
        /// 1) 生成入库头/行；
        /// 2) 回写采购行 RecQty；
        /// 3) 回写需求池 ReceivedQty；
        /// 4) 回写销售需求 FilledQty；
        /// 5) 更新虚拟库库存（WhseCode=V_WH，LocationCode=SupplierCode）。
        /// 全流程在同一事务中执行。
        /// </summary>
        public async Task<ServiceResult<SaveReceiveDetailResultDto>> PostReceiveDetialAsync(int headId)
        {
            if (headId <= 0)
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库请求不能为空");

            var head = await _receiveHeadRepository.GetByIdAsync(headId, withUpdLock: true);
            if (head == null)
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库单不存在");

            if (string.IsNullOrWhiteSpace(head.SupplierCode))
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库单缺少供应商");

            var lineList = (await _receiveLineRepository.GetListAsync(new { ReceiveNo = head.ReceiveNo }, withUpdLock: true)).ToList();
            if (lineList.Count == 0)
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库行不能为空");

            if (lineList.Any(x => string.IsNullOrWhiteSpace(x.Sku) || x.Qty <= 0))
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("存在无效入库行（SKU 为空或数量<=0）");

            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                // 中文注释：生成入库单号.
                var receiveNoResult = await _documentNoService.GetNextDocumentNoAsync(new DocumentNoRequestDto
                {
                    DocType = DocumentTypeStaticsEnum.InboundOrder,
                    BusinessDate = head.ReceiveDate ?? DateTime.Now,
                    SerialWidth = 4,
                    Operator = string.IsNullOrWhiteSpace(head.Handler) ? "system" : head.Handler
                });

                if (!receiveNoResult.Success || receiveNoResult.Data == null)
                    throw new Exception("生成入库单号失败：" + receiveNoResult.Message);

                var receiveNo = string.IsNullOrWhiteSpace(head.ReceiveNo)
                    ? receiveNoResult.Data.DocumentNo
                    : head.ReceiveNo;

                head.ReceiveNo = receiveNo;
                head.UpdatedDate = DateTime.Now;
                await _receiveHeadRepository.UpdateAsync(head);

                var lineEntities = lineList;

                // 3) 回写采购单行 RecQty（按 PoLineId 聚合，批量查询+批量更新）
                var poUpdates = lineEntities
                    .Where(x => x.PoLineId.HasValue && x.PoLineId > 0)
                    .GroupBy(x => x.PoLineId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

                if (poUpdates.Count > 0)
                {
                    var poLineEntities = (await _purchaseOrderLineRepository.GetByIdListAsync(poUpdates.Keys, withUpdLock: true)).ToList();
                    foreach (var poLine in poLineEntities)
                    {
                        if (!poUpdates.TryGetValue(poLine.Id, out var incQty))
                            continue;

                        poLine.RecQty = (poLine.RecQty <= 0 ? 0 : poLine.RecQty) + incQty;
                        poLine.LastRecDate = head.ReceiveDate ?? DateTime.Now;
                        poLine.ChangeTime = DateTime.Now;
                    }

                    if (poLineEntities.Count > 0)
                        await _purchaseOrderLineRepository.UpdateRangeAsync(poLineEntities);
                }

                // 4) 回写需求池 ReceivedQty
                // 4.1 优先路径：按 TargetLineId(=PoLineId) 精确回写到净需求行。
                // 4.2 兜底路径：若未建立 TargetLineId 关联，则按 SoLineId 比例分摊（历史兼容）。

                // 4.1 精确回写（按 TargetLineId 分组，按 RequiredDate/Id 顺序分摊）
                var demandByTargetLine = lineEntities
                    .Where(x => x.PoLineId.HasValue && x.PoLineId > 0)
                    .GroupBy(x => x.PoLineId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

                var targetLineIds = demandByTargetLine.Keys.ToList();
                var demandRowsByTarget = new Dictionary<int, List<DependentDemand>>();
                var targetLineWithDemand = new HashSet<int>();

                if (targetLineIds.Count > 0)
                {
                    var targetDemands = (await _dependRepo.GetListByTargetLineIdsAsync(targetLineIds, withUpdLock: true)).ToList();

                    demandRowsByTarget = targetDemands
                        .Where(x => x.TargetLineId.HasValue)
                        .GroupBy(x => x.TargetLineId!.Value)
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderBy(x => x.RequiredDate ?? DateTime.MaxValue)
                                  .ThenBy(x => x.Id)
                                  .ToList());

                    targetLineWithDemand = demandRowsByTarget.Keys.ToHashSet();

                    var changedDemandMap = new Dictionary<int, DependentDemand>();

                    foreach (var kv in demandByTargetLine)
                    {
                        var targetLineId = kv.Key;
                        var leftQty = kv.Value;
                        if (leftQty <= 0) continue;

                        if (!demandRowsByTarget.TryGetValue(targetLineId, out var rows) || rows.Count == 0)
                            continue;

                        foreach (var row in rows)
                        {
                            if (leftQty <= 0) break;

                            var gap = Math.Max(row.NetQty - row.ReceivedQty, 0m);
                            if (gap <= 0) continue;

                            var allocQty = Math.Min(gap, leftQty);
                            if (allocQty <= 0) continue;

                            row.ReceivedQty += allocQty;
                            // 保护：不允许溢出 NetQty
                            if (row.ReceivedQty > row.NetQty)
                                row.ReceivedQty = row.NetQty;

                            row.ChangeTime = DateTime.Now;
                            if (row.ReceivedQty >= row.NetQty)
                                row.StatusFlag = IndependentDemandStatusEnum.已到货;

                            changedDemandMap[row.Id] = row;
                            leftQty -= allocQty;
                        }
                    }

                    if (changedDemandMap.Count > 0)
                        await _dependRepo.UpdateRangeAsync(changedDemandMap.Values);
                }

                // 4.2 兜底分摊（SoLineId）
                // 中文注释：ReceiveLine 新模型已移除 SoLineId，兜底分摊入口暂关闭。
                // 如需兼容历史手工单，可在后续版本引入“行级来源需求映射表”后再开启。

                // 4.1) 回写 IndependentDemand FilledQty（按来源类型 + DemandId 聚合），避免来源类型硬编码。
                // ReceiveLine 新模型不包含 SoLineId/SourceType，IndependentDemand 回写由后续过账扩展处理。

                // 5) 更新库存：WhseCode='V_WH'、LocationCode=SupplierCode、Sku
                // 并计算每个 SKU 的 PostQty，用于写库存流水。
                var inventoryUpdates = lineEntities
                    .GroupBy(x => new { SupplierCode = head.SupplierCode.Trim(), Sku = x.Sku.Trim(), x.Unit })
                    .Select(g => new
                    {
                        g.Key.SupplierCode,
                        g.Key.Sku,
                        g.Key.Unit,
                        Qty = g.Sum(x => x.Qty)
                    })
                    .ToList();

                var inventoryTxList = new List<InventoryTransaction>();

                foreach (var inv in inventoryUpdates)
                {
                    decimal postQty;
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
                        postQty = stock.OnhandQty;
                    }
                    else
                    {
                        stock.OnhandQty += inv.Qty;
                        stock.UpdatedDate = DateTime.Now;
                        await _inventoryRepository.UpdateAsync(stock);
                        postQty = stock.OnhandQty;
                    }

                    // 中文注释：库存流水只新增，不更新；错误修正应通过红冲流水。
                    inventoryTxList.Add(new InventoryTransaction
                    {
                        WhseCode = "V_WH",
                        LocationCode = inv.SupplierCode,
                        Sku = inv.Sku,
                        BizType = BizTypesEnum.采购入库,
                        BizNo = receiveNo,
                        BizLineId = null,
                        InQty = inv.Qty,
                        OutQty = 0,
                        ChangeQty = inv.Qty,
                        PostQty = postQty,
                        UnitPrice = null,
                        Amount = null,
                        RefNo = lineEntities.Select(x => x.PoNo).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                        CreatedBy = head.Handler,
                        CreatedDate = head.ReceiveDate ?? DateTime.Now,
                        IsDeleted = false
                    });
                }

                // 6) 库存流水：走仓储新增（只增不改）。
                if (inventoryTxList.Count > 0)
                {
                    await _inventoryTransactionRepository.AddRangeAsync(inventoryTxList);
                }

                _session.Transaction.Commit();
                return ServiceResult<SaveReceiveDetailResultDto>.Ok(new SaveReceiveDetailResultDto
                {
                    Id = head.Id,
                    ReceiveNo = receiveNo
                });
            }
            catch (Exception ex)
            {
                _session.Transaction.Rollback();
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("执行采购入库失败：" + ex.Message);
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }

        public async Task<ServiceResult<SaveReceiveDetailResultDto>> SaveDetailAsync(SaveDocumentRequestBase<ReceiveHeadDto, ReceiveLineDto> request)
        {
            if (request == null)
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库请求不能为空");

            if (request.Header == null)
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库单头不能为空");

            if (string.IsNullOrWhiteSpace(request.Header.SupplierCode))
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("供应商不能为空");

            if (request.AddLines.Any(x => string.IsNullOrWhiteSpace(x.Sku) || x.Qty <= 0))
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("存在无效入库行（SKU 为空或数量<=0）");

            if (request.UpdateLines.Any(x => x.Id <= 0 || string.IsNullOrWhiteSpace(x.Sku) || x.Qty <= 0))
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("存在无效更新行（Id/SKU/Qty 不合法）");

            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                ReceiveHead? head;

                // 1) 保存/更新入库头
                if (request.Header.Id > 0)
                {
                    head = await _receiveHeadRepository.GetByIdAsync(request.Header.Id, withUpdLock: true);
                    if (head == null)
                        return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库单不存在，无法更新");

                    // 中文注释：过账制场景下，若状态已过账则禁止编辑。
                    if (string.Equals(head.Status, "已过账", StringComparison.OrdinalIgnoreCase))
                        return ServiceResult<SaveReceiveDetailResultDto>.Fail("入库单已过账，禁止修改");

                    head.SupplierCode = request.Header.SupplierCode?.Trim();
                    head.ReceiveDate = request.Header.ReceiveDate;
                    head.Handler = request.Header.Handler;
                    head.Remark = request.Header.Remark;
                    head.RelatedPoNos = request.Header.RelatedPoNos;
                    head.UpdatedDate = DateTime.Now;

                    await _receiveHeadRepository.UpdateAsync(head);
                }
                else
                {
                    var receiveNo = request.Header.ReceiveNo;
                    if (string.IsNullOrWhiteSpace(receiveNo))
                    {
                        var noRst = await _documentNoService.GetNextDocumentNoAsync(new DocumentNoRequestDto
                        {
                            DocType = DocumentTypeStaticsEnum.InboundOrder,
                            BusinessDate = request.Header.ReceiveDate ?? DateTime.Now,
                            SerialWidth = 4,
                            Operator = string.IsNullOrWhiteSpace(request.Header.Handler) ? "system" : request.Header.Handler
                        });

                        if (!noRst.Success || noRst.Data == null)
                            return ServiceResult<SaveReceiveDetailResultDto>.Fail("生成入库单号失败：" + noRst.Message);

                        receiveNo = noRst.Data.DocumentNo;
                    }

                    head = new ReceiveHead
                    {
                        ReceiveNo = receiveNo.Trim(),
                        SupplierCode = request.Header.SupplierCode?.Trim(),
                        ReceiveDate = request.Header.ReceiveDate ?? DateTime.Now,
                        Handler = request.Header.Handler,
                        Remark = request.Header.Remark,
                        RelatedPoNos = request.Header.RelatedPoNos,
                        IsDeleted = false,
                        CreatedDate = DateTime.Now
                    };

                    head.Id = await _receiveHeadRepository.AddAsync(head);
                }

                // 2) 删除行
                if (request.DeleteLineIds != null && request.DeleteLineIds.Count > 0)
                {
                    await _receiveLineRepository.DeleteRangeAsync(request.DeleteLineIds, softDelete: false);
                }

                // 3) 更新行
                if (request.UpdateLines != null && request.UpdateLines.Count > 0)
                {
                    var updateIds = request.UpdateLines.Select(x => x.Id).Distinct().ToList();
                    var dbLines = (await _receiveLineRepository.GetByIdListAsync(updateIds, withUpdLock: true)).ToDictionary(x => x.Id);
                    var changedLines = new List<ReceiveLine>();

                    foreach (var dto in request.UpdateLines)
                    {
                        if (!dbLines.TryGetValue(dto.Id, out var line))
                            continue;

                        line.ReceiveNo = head.ReceiveNo;
                        line.Sku = dto.Sku?.Trim() ?? string.Empty;
                        line.Qty = dto.Qty;
                        line.Unit = dto.Unit;
                        line.PoLineId = dto.PoLineId;
                        line.PoNo = dto.PoNo;
                        line.UpdatedDate = DateTime.Now;
                        changedLines.Add(line);
                    }

                    if (changedLines.Count > 0)
                        await _receiveLineRepository.UpdateRangeAsync(changedLines);
                }

                // 4) 新增行
                if (request.AddLines != null && request.AddLines.Count > 0)
                {
                    var addLines = request.AddLines.Select(x => new ReceiveLine
                    {
                        ReceiveNo = head.ReceiveNo,
                        Sku = x.Sku?.Trim() ?? string.Empty,
                        Qty = x.Qty,
                        Unit = x.Unit,
                        PoLineId = x.PoLineId,
                        PoNo = x.PoNo,
                        IsDeleted = false,
                        CreatedDate = DateTime.Now
                    }).ToList();

                    await _receiveLineRepository.AddRangeAsync(addLines);
                }

                // 5) 自动维护头表 RelatedPoNos（按当前全部未删除行聚合）
                var allLines = await _receiveLineRepository.GetListAsync(new { ReceiveNo = head.ReceiveNo });
                head.RelatedPoNos = string.Join(",", allLines
                    .Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.PoNo))
                    .Select(x => x.PoNo!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                head.UpdatedDate = DateTime.Now;
                await _receiveHeadRepository.UpdateAsync(head);

                _session.Transaction.Commit();
                return ServiceResult<SaveReceiveDetailResultDto>.Ok(new SaveReceiveDetailResultDto
                {
                    Id = head.Id,
                    ReceiveNo = head.ReceiveNo
                });
            }
            catch (Exception ex)
            {
                _session.Transaction.Rollback();
                return ServiceResult<SaveReceiveDetailResultDto>.Fail("保存入库单失败：" + ex.Message);
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }
    }
}
