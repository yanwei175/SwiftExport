namespace SwiftExportClient.Navigation;

/// <summary>
/// 可在 Shell 中作为多实例文档打开的 ViewModel。
/// </summary>
public interface IDocumentTabViewModel
{
    string DocumentTitle { get; }

    bool HasUnsavedChanges { get; }

    Task<bool> CanCloseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 接收文档打开参数的可选初始化入口。
/// </summary>
public interface IDocumentTabInitializable
{
    Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken);
}
