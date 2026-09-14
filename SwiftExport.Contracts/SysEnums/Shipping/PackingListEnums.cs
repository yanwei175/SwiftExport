namespace SwiftExport.Contracts.SysEnums.Shipping
{
    /// <summary>保留装箱单实体的生命周期状态，供历史数据映射使用。</summary>
    public enum PackingListLifecycleStatusEnum
    {
        Active = 0,
        Closed = 8,
        Voided = 9
    }

    /// <summary>保留装箱单实体的库存过账状态，供历史数据映射使用。</summary>
    public enum PackingListPostingStatusEnum
    {
        Unposted = 0,
        Posted = 1,
        Reversed = 8
    }

}
