using System.ComponentModel;

namespace SwiftExport.Contracts.SysEnums.Shipping
{
    /// <summary>保留装箱单实体的生命周期状态，供历史数据映射使用。</summary>
    public enum PackingListLifecycleStatusEnum
    {
        [Description("生效中")]
        Active = 0,
        [Description("已关闭")]
        Closed = 8,
        [Description("已作废")]
        Voided = 9
    }

    /// <summary>保留装箱单实体的库存过账状态，供历史数据映射使用。</summary>
    public enum PackingListPostingStatusEnum
    {
        [Description("未过账")]
        Unposted = 0,
        [Description("已过账")]
        Posted = 1,
        [Description("已冲销")]
        Reversed = 8
    }

}
