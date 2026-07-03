namespace MateWpfThemes.FW.Controls
{
    /// <summary>
    /// DataGrid 搜索列提交选择时传给业务层的通用上下文。
    /// </summary>
    public class LookupCommitContext
    {
        public object RowItem { get; set; }
        public object SelectedItem { get; set; }
        public object SelectedValue { get; set; }
        public string SearchText { get; set; } = string.Empty;
        public string ColumnKey { get; set; } = string.Empty;
        public LookupCommitReason CommitReason { get; set; }
    }
}
