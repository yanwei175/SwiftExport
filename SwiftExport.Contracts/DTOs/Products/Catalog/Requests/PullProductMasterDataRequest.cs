using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    /// <summary>指定从已关联主型号读取资料的产品标识，不携带来源字段值。</summary>
    public class PullProductMasterDataRequest
    {
        /// <summary>待拉取资料的产品标识。</summary>
        public List<Guid> ProductIds { get; set; } = new List<Guid>();
    }
}
