using SwiftExport.Contracts.DTOs.Customers;

namespace SwiftExport.View.FW.CustomerViews.Messages
{
    /// <summary>
    /// 客户选择结果消息，通过 MvvmLight Messenger 在 ViewModel 与 Dialog 之间传递结果。
    /// Result 为 null 表示用户取消；否则为用户确认的客户对象。
    /// </summary>
    public class CustomerSelectionResultMessage
    {
        /// <summary>
        /// 用户最终确认的客户；null 表示取消选择。
        /// </summary>
        public CustomerDto Result { get; }

        public CustomerSelectionResultMessage(CustomerDto result)
        {
            Result = result;
        }
    }
}
