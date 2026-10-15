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
        /// <summary>可选物料类型过滤；指定包材时服务端始终只返回启用包材，省略保持普通候选行为。</summary>
        public string MaterialType { get; set; }
        /// <summary>仅取启用物料；默认不改变已有普通物料候选的启停口径。</summary>
        public bool ActiveOnly { get; set; }
    }
}
