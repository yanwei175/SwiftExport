using SwiftExport.Contracts.SysEnums.Inventory;
using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public static class InboundPostingStatusList
    {
        /// <summary>过账状态</summary>
        public static IReadOnlyList<InboundPostingStatusEnum> All { get; } =
            new List<InboundPostingStatusEnum>
            {
                InboundPostingStatusEnum.Unposted,
                InboundPostingStatusEnum.Posted,
                InboundPostingStatusEnum.Reversed
            }.AsReadOnly();
    }
}
