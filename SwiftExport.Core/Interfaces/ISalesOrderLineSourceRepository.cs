using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 销售订单行数据源仓储接口。
/// 负责 CustomerProduct + Product 联查分页。
/// </summary>
public interface ISalesOrderLineSourceRepository
{
    ///// <summary>
    ///// 联查分页：返回可直接映射为 SalesOrderLineDto 的读模型。
    ///// </summary>
    //Task<PagedResult<SalesOrderLineDto>> GetPagedSalesOrderLineSourceDtoAsync(
    //    QueryCriteria criteria,
    //    int pageIndex,
    //    int pageSize,
    //    string orderBy = "cp.Id DESC");

    /// <summary>
    /// 销售产品分页查询。
    /// 直接接收 SalesProductSearchDto，由仓储层按业务规则解析：
    /// 1. CustomerId/CustomerCode 决定是否联 CustomerProduct；
    /// 2. BatchCustomerSkuList 做客户SKU精确匹配；
    /// 3. BatchKeywords 做 SKU/描述/特性/客户SKU 模糊匹配。
    /// </summary>
    Task<PagedResult<SalesProductDto>> GetSalesProductPagedListAsync(
        SalesProductSearchDto request,
        string orderBy = "cp.Id DESC");
}
