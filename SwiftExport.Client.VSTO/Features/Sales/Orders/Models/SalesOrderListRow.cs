using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    /// <summary>组合只读列表 DTO 与本页勾选状态，不修改服务器状态或复制传输字段。</summary>
    public sealed class SalesOrderListRow : ObservableObject
    {
        private bool isChecked;
        /// <summary>保留搜索返回的只读单据摘要；后续生成 Excel 可用其 Uid 加载详情。</summary>
        public SalesOrderManagerListItemDto Order { get; }
        /// <summary>本页批量生成预留选择，独立于 DataGrid 当前行。</summary>
        public bool IsChecked { get { return isChecked; } set { SetProperty(ref isChecked, value); } }
        /// <summary>显示确认状态，未知枚举不推断为已确认。</summary>
        public string ConfirmationText => Order.ConfirmationStatus == ConfirmationStatusEnum.已确认 ? "已确认"
            : Order.ConfirmationStatus == ConfirmationStatusEnum.待确认 ? "待确认" : "未知";
        /// <summary>显示单据生命周期。</summary>
        public string DocumentText => Order.DocumentStatus == SalesOrderDocumentStatusEnum.Open ? "开放"
            : Order.DocumentStatus == SalesOrderDocumentStatusEnum.Closed ? "已关闭" : "未知";
        /// <summary>使用查询返回的履约状态；无证据时保持未知。</summary>
        public string FulfillmentText
        {
            get
            {
                switch (Order.FulfillmentStatus)
                {
                    case SalesOrderFulfillmentStatusEnum.NotStarted: return "未开始";
                    case SalesOrderFulfillmentStatusEnum.PartiallyShipped: return "部分发货";
                    case SalesOrderFulfillmentStatusEnum.FullyShipped: return "全部发货";
                    default: return "未知";
                }
            }
        }
        /// <summary>引用当前查询行，不改变 DTO 的数量、单位或空值语义。</summary>
        public SalesOrderListRow(SalesOrderManagerListItemDto order) { Order = order; }
    }
}
