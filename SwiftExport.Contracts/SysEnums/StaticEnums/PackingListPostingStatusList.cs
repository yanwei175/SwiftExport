using SwiftExport.Contracts.SysEnums.Shipping;
using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public static class PackingListPostingStatusList
    {        /// <summary>过账状态</summary>
        public static IReadOnlyList<PackingListPostingStatusEnum> All { get; } =
            new List<PackingListPostingStatusEnum>
            {
                PackingListPostingStatusEnum.Unposted,
                PackingListPostingStatusEnum.Posted,
                PackingListPostingStatusEnum.Reversed
            }.AsReadOnly();
    }
}
