using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders
{
    /// <summary>
    /// 销售订单行数据源服务。
    /// 提供“CustomerProduct + Product 联查”的分页查询能力，
    /// 为订单行编辑界面提供可直接回填的基础数据。
    /// </summary>
    public class SalesOrderLineSourceService(ISalesOrderLineSourceRepository repository) : ISalesOrderLineSourceService
    {
        private readonly ISalesOrderLineSourceRepository _repository = repository;

        /// <summary>
        /// 分页查询订单行候选数据。
        /// </summary>
        //public async Task<ServiceResult<PagedResultDto<SalesOrderLineDto>>> GetPagedSalesOrderLineSourceDtoAsync(SalesOrderLineSourceSearchDto search)
        //{
        //    search ??= new SalesOrderLineSourceSearchDto();

        //    // 中文注释：业务调整后 CustomerId 允许为空（<=0）。
        //    // - CustomerId > 0：走客户产品维度（CustomerProduct + Product）。
        //    // - CustomerId <= 0：走产品主数据维度（以 Product 为主）。

        //    var criteria = BuildCriteria(search);

        //    int pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
        //    int pageSize = search.PageSize > 0 ? search.PageSize : 20;

        //    var paged = await _repository.GetPagedSalesOrderLineSourceDtoAsync(criteria, pageIndex, pageSize, "cp.Id DESC");

        //    return ServiceResult<PagedResultDto<SalesOrderLineDto>>.Ok(new PagedResultDto<SalesOrderLineDto>
        //    {
        //        Items = [.. paged.Items],
        //        TotalCount = paged.TotalCount,
        //        PageIndex = paged.PageIndex,
        //        PageSize = paged.PageSize
        //    });
        //}

        /// <summary>
        /// 构建查询条件。
        /// </summary>
        private static QueryCriteria BuildCriteria(SalesOrderLineSourceSearchDto search)
        {
            var criteria = new QueryCriteria();
            if (search.CustomerId > 0)
            {
                // 必选客户过滤
                criteria.Add("CustomerId", 查询操作符.等于, search.CustomerId);

            }

            // 可选客户编码过滤（用于双保险）
            if (!string.IsNullOrWhiteSpace(search.CustomerCode))
            {
                criteria.Add("CustomerCode", 查询操作符.等于, search.CustomerCode.Trim());
            }

            // 批量关键字：换行/逗号分割，匹配 SKU 或 客户型号
            if (!string.IsNullOrWhiteSpace(search.BatchKeywords))
            {
                var batchList = search.BatchKeywords
                    .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (batchList.Count > 0)
                {
                    criteria.AddOrGroup(
                        ("Sku", 查询操作符.包含某值, batchList),
                        ("CustomerSku", 查询操作符.包含某值, batchList));
                }
            }

            // 客户SKU精确匹配：换行/逗号分割后按精确值过滤 CustomerSku
            if (!string.IsNullOrWhiteSpace(search.ExactCustomerSkuBatch))
            {
                var exactCustomerSkuList = search.ExactCustomerSkuBatch
                    .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (exactCustomerSkuList.Count > 0)
                {
                    criteria.Add("CustomerSku", 查询操作符.包含某值, exactCustomerSkuList);
                }
            }

            // 单关键字：模糊匹配 SKU / 客户型号 / 产品描述 / 产品特性 / 颜色 / 关联SKU
            if (!string.IsNullOrWhiteSpace(search.Keyword))
            {
                var keyword = search.Keyword.Trim();
                criteria.AddOrGroup(
                    ("Sku", 查询操作符.模糊匹配, keyword),
                    ("CustomerSku", 查询操作符.模糊匹配, keyword),
                    // 中文注释：以下三个字段来自联表 Product / ProductColor，
                    // 通过带别名字段名传入，仓储层会按原字段解析。
                    ("p.DescriptionEn", 查询操作符.模糊匹配, keyword),
                    ("p.FeaturesEn", 查询操作符.模糊匹配, keyword),
                    ("pc.ColorNameEn", 查询操作符.模糊匹配, keyword),
                    ("LinkSku", 查询操作符.模糊匹配, keyword));
            }

            return criteria;
        }
        public async Task<ServiceResult<PagedResultDto<SalesProductDto>>> GetSalesProductPagedListAsync(SalesProductSearchDto request)
        {
            try
            {
                request ??= new SalesProductSearchDto();

                // 中文注释：服务层仅做请求参数标准化，具体 SQL 条件由仓储层直解析 SalesProductSearchDto。
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

                var rst = await _repository.GetSalesProductPagedListAsync(request);
                return ServiceResult<PagedResultDto<SalesProductDto>>.Ok(new PagedResultDto<SalesProductDto>
                {
                    Items = rst.Items,
                    TotalCount = rst.TotalCount,
                    PageIndex = rst.PageIndex,
                    PageSize = rst.PageSize
                });
            }
            catch (Exception ex)
            {
                return ServiceResult<PagedResultDto<SalesProductDto>>.Fail("查询失败: " + ex.Message);
            }

        }
    }
}
