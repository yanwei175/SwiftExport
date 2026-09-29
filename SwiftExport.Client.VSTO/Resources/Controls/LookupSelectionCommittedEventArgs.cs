using System;

namespace SwiftExport.Client.VSTO.Resources.Controls
{
    /// <summary>提供候选提交时的选择快照；不包含缓存或页面业务行为。</summary>
    public sealed class LookupSelectionCommittedEventArgs : EventArgs
    {
        /// <summary>记录已选对象、选择值、搜索文字及提交来源。</summary>
        public LookupSelectionCommittedEventArgs(object selectedItem, object selectedValue,
            string searchText, LookupCommitReason commitReason)
        {
            SelectedItem = selectedItem;
            SelectedValue = selectedValue;
            SearchText = searchText ?? string.Empty;
            CommitReason = commitReason;
        }

        /// <summary>已提交的候选对象。</summary>
        public object SelectedItem { get; }
        /// <summary>由 SelectedValuePath 提取的值。</summary>
        public object SelectedValue { get; }
        /// <summary>提交时的搜索文字。</summary>
        public string SearchText { get; }
        /// <summary>提交来自何种用户操作。</summary>
        public LookupCommitReason CommitReason { get; }
    }
}
