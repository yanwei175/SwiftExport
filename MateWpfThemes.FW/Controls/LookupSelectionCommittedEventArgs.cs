using System;

namespace MateWpfThemes.FW.Controls
{
    /// <summary>
    /// SearchableTextBoxPlus 选择提交事件参数。
    /// </summary>
    public class LookupSelectionCommittedEventArgs : EventArgs
    {
        public LookupSelectionCommittedEventArgs(object selectedItem, object selectedValue, string searchText, LookupCommitReason commitReason)
        {
            SelectedItem = selectedItem;
            SelectedValue = selectedValue;
            SearchText = searchText ?? string.Empty;
            CommitReason = commitReason;
        }

        public object SelectedItem { get; }
        public object SelectedValue { get; }
        public string SearchText { get; }
        public LookupCommitReason CommitReason { get; }
    }
}
