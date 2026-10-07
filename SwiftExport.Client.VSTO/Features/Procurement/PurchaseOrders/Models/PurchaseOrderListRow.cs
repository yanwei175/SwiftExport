using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;

namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models
{
    /// <summary>组合采购管理只读 DTO 与当前页勾选状态，不改变服务器数量或状态。</summary>
    public sealed class PurchaseOrderListRow : ObservableObject
    {
        private bool isChecked;
        /// <summary>引用管理查询摘要；后续 Excel 生成使用 PurchaseOrderId 加载详情。</summary>
        public PurchaseOrderManagerListItemDto Order { get; }
        /// <summary>独立于当前行的本页批量选择，不进入共享契约。</summary>
        public bool IsChecked { get { return isChecked; } set { SetProperty(ref isChecked, value); } }
        /// <summary>显示服务端确认状态。</summary>
        public string ConfirmationText => Describe(Order.ConfirmationStatus);
        /// <summary>显示活动、关闭或作废生命周期，不将确认状态合并为单据状态。</summary>
        public string DocumentText => Describe(Order.LifecycleStatus);
        /// <summary>展示累计货好查询状态，不依据已知数量推导整单货好。</summary>
        public string ReadyText => Describe(Order.ReadyStatus);
        /// <summary>展示正式净收货查询状态，不代表库存余额。</summary>
        public string ReceiptText => Describe(Order.ReceiptStatus);
        /// <summary>保持 DTO 原数量、单位、空值和风险语义。</summary>
        public PurchaseOrderListRow(PurchaseOrderManagerListItemDto order) { Order = order; }

        // 使用共享枚举的中文描述，未知值明确显示未知，不推导为成功状态。
        private static string Describe(Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var description = field == null ? null : Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
            return description?.Description ?? "未知";
        }
    }
}
