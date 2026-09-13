namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>入库建立方式；与供货单据类型独立，不参与来源容量分组。</summary>
    public enum InboundCreationModeEnum
    {
        /// <summary>人工选源建立。</summary>
        [System.ComponentModel.Description("人工")]
        Manual = 0,
        /// <summary>其他业务流程自动触发建立。</summary>
        [System.ComponentModel.Description("自动")]
        Automatic = 1
    }
}
