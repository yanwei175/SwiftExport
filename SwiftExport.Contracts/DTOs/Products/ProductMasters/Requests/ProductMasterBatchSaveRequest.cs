using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>主型号管理页批量保存请求。</summary>
    public class ProductMasterBatchSaveRequest : BatchSaveRequestBase<ProductMasterWriteDto>
    {
        /// <summary>是否用主型号描述与配置覆盖关联物料，包含空值清除；默认不同步。</summary>
        public bool UpdateProductDescriptions { get; set; }
        /// <summary>是否用主型号默认供应商覆盖关联物料，包含空值清除；默认不同步。</summary>
        public bool UpdateProductDefaultSupplier { get; set; }
    }
}
