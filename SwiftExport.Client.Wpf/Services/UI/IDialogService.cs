namespace SwiftExportClient.Services.UI;

public interface IDialogService
{
    Task ShowContentAsync(
        string title,
        object content,
        string closeButtonText = "关闭",
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText = "确定",
        string closeButtonText = "取消",
        CancellationToken cancellationToken = default);

    Task ShowErrorAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);
}
