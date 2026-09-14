namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>入库过账状态，与生命周期独立。</summary>
    public enum InboundPostingStatusEnum
    {
        [System.ComponentModel.Description("未过账")]
        Unposted = 0,
        [System.ComponentModel.Description("已过账")]
        Posted = 1,
        [System.ComponentModel.Description("反过账")]
        Reversed = 8
    }

}

