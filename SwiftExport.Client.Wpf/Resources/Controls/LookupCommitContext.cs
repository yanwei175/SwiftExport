namespace SwiftExportClient.Controls;

public sealed class LookupCommitContext
{
    public object? RowItem { get; set; }

    public object? SelectedItem { get; set; }

    public object? SelectedValue { get; set; }

    public string SearchText { get; set; } = string.Empty;

    public string ColumnKey { get; set; } = string.Empty;

    public LookupCommitReason CommitReason { get; set; }
}
