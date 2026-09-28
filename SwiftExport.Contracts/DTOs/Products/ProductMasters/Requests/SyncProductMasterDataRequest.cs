namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>指定需要从数据库当前主型号资料向关联产品下发的主型号标识。</summary>
    public class SyncProductMasterDataRequest
    {
        /// <summary>待下发的主型号标识；目标产品由现有关联自动确定。</summary>
        public System.Collections.Generic.List<System.Guid> ProductMasterIds { get; set; } = new System.Collections.Generic.List<System.Guid>();
    }
}
