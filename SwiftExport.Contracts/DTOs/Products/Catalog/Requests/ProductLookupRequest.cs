using System;

namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    /// <summary>
    /// 产品搜索选择器请求。Limit 由服务端限制为 1 到 50，默认 20。
    /// </summary>
    public class ProductLookupRequest
    {
        public string Keyword { get; set; }
        public Guid ExcludeProductId { get; set; }
        public int Limit { get; set; } = 20;
    }
}
