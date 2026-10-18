using System;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Customers.Requests
{
    /// <summary>按客户、供应商身份及编码关键词分页查询采购默认配置，各条件同时生效。</summary>
    public class CustomerPoConfigSearchRequest : PagedRequestBase
    {
        /// <summary>精确筛选客户主数据身份；空值表示不限制客户。</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>精确筛选供应商主数据身份；空值表示不限制供应商。</summary>
        public Guid? SupplierId { get; set; }
    }
}
