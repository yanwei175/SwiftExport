namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>普通单据生命周期；旧单据不自动迁移到此枚举。</summary>
    public enum DocumentStatusEnum : byte
    {
        /// <summary>开放。</summary>
        [System.ComponentModel.Description("开放")]
        Open = 0,
        /// <summary>关闭，售后显示为已结案。</summary>
        [System.ComponentModel.Description("已关闭")]
        Closed = 8,
        /// <summary>作废。</summary>
        [System.ComponentModel.Description("已作废")]
        Voided = 9
    }
}
