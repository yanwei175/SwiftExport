using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>主型号详情聚合保存请求。</summary>
    public class SaveProductMasterDetailRequest
    {
        /// <summary>是否用主型号描述与配置覆盖关联物料，包含空值清除；默认不同步。</summary>
        public bool UpdateProductDescriptions { get; set; }
        /// <summary>是否用主型号默认供应商覆盖关联物料，包含空值清除；默认不同步。</summary>
        public bool UpdateProductDefaultSupplier { get; set; }
        /// <summary>获取或设置主型号标识；空值表示新建。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>获取或设置主型号期望版本；新建时为零。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>获取或设置主型号头部数据。</summary>
        public ProductMasterWriteDto Master { get; set; } = new ProductMasterWriteDto();
        /// <summary>获取或设置新增中性产品。</summary>
        public List<CreateItemRequest<ProductNeutralWriteDto>> CreateNeutrals { get; set; } = new List<CreateItemRequest<ProductNeutralWriteDto>>();
        /// <summary>获取或设置修改中性产品。</summary>
        public List<UpdateItemRequest<ProductNeutralWriteDto>> UpdateNeutrals { get; set; } = new List<UpdateItemRequest<ProductNeutralWriteDto>>();
        /// <summary>获取或设置删除中性产品。</summary>
        public List<DeleteItemRequest> DeleteNeutrals { get; set; } = new List<DeleteItemRequest>();
    }
}
