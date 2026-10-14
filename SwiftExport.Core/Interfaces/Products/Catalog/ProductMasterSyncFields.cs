namespace SwiftExport.Core.Interfaces.Products.Catalog;

/// <summary>限定物料主动获取主型号时允许覆盖的资料组；由服务端固定选择，不能由客户端传入。</summary>
[Flags]
public enum ProductMasterSyncFields
{
    /// <summary>不更新产品资料。</summary>
    None = 0,
    /// <summary>中英文描述。</summary>
    Descriptions = 1,
    /// <summary>中英文配置。</summary>
    Features = 2,
    /// <summary>默认供应商标识。</summary>
    Supplier = 4,
    /// <summary>毛重和体积，直接写入产品单位字段。</summary>
    WeightVolume = 8,
    /// <summary>同时获取四组资料。</summary>
    All = Descriptions | Features | Supplier | WeightVolume
}
