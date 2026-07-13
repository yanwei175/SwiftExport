namespace SwiftExportClient.Resources.Controls;

public sealed class LookupSelectionCommittedEventArgs(
    object? selectedItem,
    object? selectedValue,
    string? searchText,
    LookupCommitReason commitReason) : EventArgs
{
    public object? SelectedItem { get; } = selectedItem;

    public object? SelectedValue { get; } = selectedValue;

    public string SearchText { get; } = searchText ?? string.Empty;

    public LookupCommitReason CommitReason { get; } = commitReason;
}
