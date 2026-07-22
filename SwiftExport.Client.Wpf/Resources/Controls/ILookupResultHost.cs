using System.Collections;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// LookupTextBox 自定义候选视图的内部交互契约。
/// </summary>
internal interface ILookupResultHost
{
    bool HasItems { get; }

    object? CurrentItem { get; }

    void Refresh(IEnumerable? itemsSource, string query);

    void SelectFirst();

    void MoveSelection(int offset);

    bool TryResolveExact(string text, out object? item);
}
