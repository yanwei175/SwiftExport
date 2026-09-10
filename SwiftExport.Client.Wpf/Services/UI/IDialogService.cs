namespace SwiftExportClient.Services.UI;

/// <summary>为 ViewModel 和 Feature UI 服务提供不依赖具体 WPF-UI 控件的统一弹窗入口。</summary>
public interface IDialogService
{
    /// <summary>展示带明确按钮结果、尺寸和滚动策略的内容弹窗。</summary>
    /// <remarks>默认实现仅用于保持既有测试替身的源码兼容；支持结构化弹窗的实现必须覆盖此方法。</remarks>
    Task<DialogResult> ShowAsync(
        object content,
        DialogOptions options,
        IDialogContentState? state = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<DialogResult>(
            new NotSupportedException("当前 DialogService 实现不支持结构化内容弹窗。"));

    /// <summary>展示只有关闭按钮的内容弹窗。</summary>
    Task ShowContentAsync(
        string title,
        object content,
        string closeButtonText = "关闭",
        CancellationToken cancellationToken = default);

    /// <summary>展示确认弹窗，仅在用户选择主按钮时返回 <see langword="true"/>。</summary>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText = "确定",
        string closeButtonText = "取消",
        CancellationToken cancellationToken = default);

    /// <summary>展示只有关闭按钮的错误消息弹窗。</summary>
    Task ShowErrorAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);
}
