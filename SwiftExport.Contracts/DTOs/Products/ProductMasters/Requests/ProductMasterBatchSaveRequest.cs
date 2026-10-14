using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>主型号管理页批量变更，仅维护主型号资料。</summary>
    public class ProductMasterBatchSaveRequest : BatchSaveRequestBase<ProductMasterWriteDto>
    {
        /// <summary>保存协议版本；系列字典身份版客户端明确提交 3，省略或旧版本拒绝写入。</summary>
        public int ContractVersion { get; set; }
    }
}
