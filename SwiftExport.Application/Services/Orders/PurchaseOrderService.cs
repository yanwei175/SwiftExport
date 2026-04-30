using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders
{
    /// <summary>
    /// 采购订单应用服务。
    /// </summary>
    public class PurchaseOrderService(
        IPurchaseOrderRepository headRepository,
        IPurchaseOrderLineRepository lineRepository,
        ISupplierRepository supplierRepository,
        ServiceBaseContext context)
        : BaseService<PurchaseOrderHead, PurchaseOrderDto>(headRepository, context), IPurchaseOrderService
    {
        private readonly IPurchaseOrderRepository _headRepository = headRepository;
        private readonly IPurchaseOrderLineRepository _lineRepository = lineRepository;
        private readonly ISupplierRepository _supplierRepository = supplierRepository;

        public async Task<ServiceResult<PagedResultDto<PurchaseOrderDto>>> GetPagedListAsync(PurchaseOrderSearchDto search)
        {
            search = search ?? new PurchaseOrderSearchDto();

            var criteria = new QueryCriteria();
            if (!string.IsNullOrWhiteSpace(search.OrderNo))
                criteria.Add("OrderNo", 查询操作符.模糊匹配, search.OrderNo.Trim());
            if (!string.IsNullOrWhiteSpace(search.PoNo))
                criteria.Add("PoNo", 查询操作符.模糊匹配, search.PoNo.Trim());
            if (!string.IsNullOrWhiteSpace(search.SalesOrderNo))
                criteria.Add("SalesOrderNo", 查询操作符.模糊匹配, search.SalesOrderNo.Trim());
            if (!string.IsNullOrWhiteSpace(search.SupplierCode))
                criteria.Add("SupplierCode", 查询操作符.等于, search.SupplierCode.Trim());
            if (!string.IsNullOrWhiteSpace(search.Status))
                criteria.Add("Status", 查询操作符.等于, search.Status.Trim());
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

            return ServiceResult<PagedResultDto<PurchaseOrderDto>>.Ok(new PagedResultDto<PurchaseOrderDto>
            {
                Items = paged.Items.Adapt<List<PurchaseOrderDto>>(),
                TotalCount = paged.TotalCount,
                PageIndex = paged.PageIndex,
                PageSize = paged.PageSize
            });
        }

        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByIdAsync(int id)
        {
            var header = await _headRepository.GetByIdAsync(id);
            if (header == null)
                return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单不存在");

            return await BuildDetailByOrderNoAsync(header.OrderNo);
        }

        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单号不能为空");

            return await BuildDetailByOrderNoAsync(orderNo.Trim());
        }

        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByPoNoAsync(string poNo)
        {
            if (string.IsNullOrWhiteSpace(poNo))
                return ServiceResult<PurchaseOrderDetailDto>.Fail("自定义编号不能为空");

            var headers = await _headRepository.GetListAsync(new { PoNo = poNo.Trim() });
            var header = headers.FirstOrDefault();
            if (header == null)
                return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单不存在");

            return await BuildDetailByOrderNoAsync(header.OrderNo);
        }

        public async Task<ServiceResult<int>> SaveDetailAsync(PurchaseOrderDetailDto request)
        {
            if (request == null || request.Header == null)
                return ServiceResult<int>.Fail("采购单头不能为空");

            var headerEntity = request.Header.Adapt<PurchaseOrderHead>();
            if (string.IsNullOrWhiteSpace(headerEntity.OrderNo))
                return ServiceResult<int>.Fail("采购单号不能为空");

            // 中文注释：PoNo 为用户自定义编号，允许为空；若有值仅作展示，不参与主链路关联。
            if (!string.IsNullOrWhiteSpace(headerEntity.PoNo))
                headerEntity.PoNo = headerEntity.PoNo.Trim();

            // 中文注释：若请求未显式传币种，则从供应商档案补默认币种，避免前端二次查档案。
            if (string.IsNullOrWhiteSpace(headerEntity.Currency) && !string.IsNullOrWhiteSpace(headerEntity.SupplierCode))
            {
                var supplier = await _supplierRepository.GetByCodeAsync(headerEntity.SupplierCode.Trim());
                headerEntity.Currency = supplier == null ? headerEntity.Currency : supplier.Currency;
            }

            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                PurchaseOrderHead persistedHeader;
                if (headerEntity.Id > 0)
                {
                    persistedHeader = await _headRepository.GetByIdAsync(headerEntity.Id);
                    if (persistedHeader == null)
                    {
                        _session.Transaction.Rollback();
                        return ServiceResult<int>.Fail("采购单不存在，无法更新");
                    }

                    headerEntity.CreatedDate = persistedHeader.CreatedDate;
                    await _headRepository.UpdateAsync(headerEntity);
                    persistedHeader = headerEntity;
                }
                else
                {
                    var existingByOrderNo = await _headRepository.GetListAsync(new { OrderNo = headerEntity.OrderNo.Trim() });
                    persistedHeader = existingByOrderNo.FirstOrDefault();
                    if (persistedHeader != null)
                    {
                        headerEntity.Id = persistedHeader.Id;
                        headerEntity.CreatedDate = persistedHeader.CreatedDate;
                        await _headRepository.UpdateAsync(headerEntity);
                        persistedHeader = headerEntity;
                    }
                    else
                    {
                        var newId = await _headRepository.AddAsync(headerEntity);
                        headerEntity.Id = newId;
                        persistedHeader = headerEntity;
                    }
                }

                var orderNo = persistedHeader.OrderNo;
                var oldLines = await _lineRepository.GetListAsync(new { OrderNo = orderNo });
                if (oldLines.Count > 0)
                {
                    await _lineRepository.DeleteRangeAsync(oldLines.Select(x => x.Id).ToList(), false);
                }

                var lineEntities = (request.Lines ?? new List<PurchaseOrderLineDto>())
                    .Select(x =>
                    {
                        var line = x.Adapt<PurchaseOrderLine>();
                        line.OrderNo = orderNo;
                        return line;
                    })
                    .ToList();

                if (lineEntities.Count > 0)
                {
                    await _lineRepository.AddRangeAsync(lineEntities);
                }

                _session.Transaction.Commit();
                return ServiceResult<int>.Ok(persistedHeader.Id);
            }
            catch (Exception ex)
            {
                _session.Transaction.Rollback();
                return ServiceResult<int>.Fail("保存采购单失败：" + ex.Message);
            }
            finally
            {
                _session.Transaction.Dispose();
                _session.Transaction = null;
            }
        }

        private async Task<ServiceResult<PurchaseOrderDetailDto>> BuildDetailByOrderNoAsync(string orderNo)
        {
            var headers = await _headRepository.GetListAsync(new { OrderNo = orderNo });
            var header = headers.FirstOrDefault();
            if (header == null)
                return ServiceResult<PurchaseOrderDetailDto>.Fail("采购单不存在");

            var lines = await _lineRepository.GetListAsync(new { OrderNo = orderNo });
            var detail = new PurchaseOrderDetailDto
            {
                Header = header.Adapt<PurchaseOrderDto>(),
                Lines = lines.Adapt<List<PurchaseOrderLineDto>>()
            };
            return ServiceResult<PurchaseOrderDetailDto>.Ok(detail);
        }
    }
}
