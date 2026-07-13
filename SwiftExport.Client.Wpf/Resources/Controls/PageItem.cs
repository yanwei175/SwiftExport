namespace SwiftExportClient.Resources.Controls;

public sealed class PageItem(int pageIndex, int currentIndex)
{
    public int PageIndex { get; } = pageIndex;

    public bool IsCurrent { get; } = pageIndex == currentIndex;

    public bool IsClickable => PageIndex != -1 && !IsCurrent;

    public string DisplayText => PageIndex == -1 ? "..." : PageIndex.ToString();
}
