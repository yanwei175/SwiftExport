using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;

namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models
{
    /// <summary>组合服务器装箱单摘要与当前页勾选状态，不复制或修改传输字段。</summary>
    public sealed class PackingListRow : ObservableObject
    {
        private bool isChecked;
        /// <summary>查询返回的只读摘要，后续导出可按 Uid 获取详情。</summary>
        public PackingListManagerListItemDto Document { get; }
        /// <summary>当前页批量导出的预留勾选，独立于表格当前行。</summary>
        public bool IsChecked { get => isChecked; set => SetProperty(ref isChecked, value); }
        /// <summary>显示服务端过账状态，未知值不推断为已过账。</summary>
        public string PostingStatusText => Document.PostingStatus == PackingListPostingStatusEnum.Unposted ? "未过账"
            : Document.PostingStatus == PackingListPostingStatusEnum.Posted ? "已过账"
            : Document.PostingStatus == PackingListPostingStatusEnum.Reversed ? "已冲销" : "未知";
        /// <summary>引用一条查询摘要，不改变其客户快照或发票汇总。</summary>
        public PackingListRow(PackingListManagerListItemDto document)
        { Document = document ?? throw new ArgumentNullException(nameof(document)); }
    }
}
