using System;

namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>产品资料维护可选的活动仓库身份和显示文字，不暴露仓库管理字段。</summary>
    public class ProductDefaultWarehouseCandidateDto
    {
        /// <summary>仓库身份，供产品默认仓库逻辑关联保存。</summary>
        public Guid Uid { get; set; }
        /// <summary>当前仓库编码。</summary>
        public string Code { get; set; }
        /// <summary>当前仓库名称。</summary>
        public string Name { get; set; }
    }
}
