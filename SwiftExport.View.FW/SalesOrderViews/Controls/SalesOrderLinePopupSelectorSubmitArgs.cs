using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.SalesOrders;

namespace SwiftExport.View.FW.SalesOrderViews.Controls
{
    /// <summary>
    /// 客户产品 Popup 选择结果参数。
    /// </summary>
    public class SalesOrderLinePopupSelectorSubmitArgs
    {
        /// <summary>
        /// 当前行上下文对象（通常是 SalesOrderLineModel）。
        /// </summary>
        public object RowContext { get; set; }

        /// <summary>
        /// Popup 中选中的候选项（客户产品 DTO）。
        /// </summary>
        public SalesOrderLineDto SelectedItem { get; set; }

        /// <summary>
        /// 对外统一暴露的 DTO（基类），避免外层强耦合到具体类型。
        /// </summary>
        public BaseDto SelectedDto => SelectedItem;
    }
}
