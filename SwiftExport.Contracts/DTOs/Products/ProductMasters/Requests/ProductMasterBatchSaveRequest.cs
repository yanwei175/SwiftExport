using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>主型号管理页批量变更及本批次对关联物料的可选覆盖指令。</summary>
    public class ProductMasterBatchSaveRequest : BatchSaveRequestBase<ProductMasterWriteDto>
    {
        /// <summary>保存协议版本；系列字典身份版客户端明确提交 3，省略或旧版本拒绝写入。</summary>
        public int ContractVersion { get; set; }
        /// <summary>是否用主型号的中英文描述、配置覆盖每个关联物料，包含空值清除；默认不同步。</summary>
        public bool UpdateProductDescriptions { get; set; }
        /// <summary>是否用主型号默认供应商 ID 覆盖每个关联物料，空 Guid 会清为 null；默认不同步。</summary>
        public bool UpdateProductDefaultSupplier { get; set; }
    }
}
