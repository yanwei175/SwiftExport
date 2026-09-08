namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>普通单据生命周期；旧单据不自动迁移到此枚举。</summary>
    public enum DocumentStatusEnum : byte
    {
        /// <summary>开放。</summary>
        Open = 0,
        /// <summary>关闭，售后显示为已结案。</summary>
        Closed = 8,
        /// <summary>作废。</summary>
        Voided = 9
    }
}

