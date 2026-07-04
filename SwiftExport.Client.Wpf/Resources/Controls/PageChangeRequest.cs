namespace SwiftExportClient.Controls;

public sealed class PageChangeRequest
{
    public int OldIndex { get; set; }

    public int NewIndex { get; set; }

    public int OldSize { get; set; }

    public int NewSize { get; set; }

    public bool IsPageSizeChanged => OldSize != NewSize;
}
