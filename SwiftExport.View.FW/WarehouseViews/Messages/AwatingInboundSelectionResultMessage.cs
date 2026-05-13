using SwiftExport.Contracts.DTOs.Orders;
using System.Collections.Generic;

namespace SwiftExport.View.FW.WarehouseViews.Messages
{
    /// <summary>
    /// 待入库选择窗口结果消息。
    /// Result 为 null 表示取消；否则为确认后的已选行。
    /// </summary>
    public class AwatingInboundSelectionResultMessage
    {
        public List<AwatingInboundDto> Result { get; }

        public AwatingInboundSelectionResultMessage(List<AwatingInboundDto> result)
        {
            Result = result;
        }
    }
}
