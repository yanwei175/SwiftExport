using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WpfContentDialogButton = Wpf.Ui.Controls.ContentDialogButton;
using WpfContentDialogResult = Wpf.Ui.Controls.ContentDialogResult;

namespace SwiftExportClient.Services.UI;

/// <summary>使用 WPF-UI ContentDialogHost 统一创建内容弹窗，并负责宿主边界、滚动策略和事件生命周期。</summary>
public sealed class WpfUiDialogService(IContentDialogService contentDialogService) : IDialogService
{
    private int _isShowing;

    /// <inheritdoc/>
    public async Task<DialogResult> ShowAsync(
        object content,
        DialogOptions options,
        IDialogContentState? state = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        ValidateDimensions(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _isShowing, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "当前 DialogService 已有活动弹窗；同一 ContentDialogHost 不支持嵌套或并发展示。");
        }

        ContentDialogHost? host;
        try
        {
            host = contentDialogService.GetDialogHostEx();
        }
        catch
        {
            Interlocked.Exchange(ref _isShowing, 0);
            throw;
        }

        if (host is null)
        {
            Interlocked.Exchange(ref _isShowing, 0);
            throw new InvalidOperationException(
                "尚未注册 WPF-UI ContentDialogHost，无法展示弹窗。请先在窗口 Loaded 阶段设置宿主。");
        }

        ContentDialog dialog;
        try
        {
            dialog = CreateDialog(content, options, state);
        }
        catch
        {
            Interlocked.Exchange(ref _isShowing, 0);
            throw;
        }
        var completed = false;
        FrameworkElement? dialogShell = null;

        void ApplyCurrentSize() => ApplyHostBounds(dialog, dialogShell, host, options);
        void OnHostSizeChanged(object sender, SizeChangedEventArgs args) => ApplyCurrentSize();
        void OnDialogLoaded(object sender, RoutedEventArgs args)
        {
            dialogShell = FindDialogShell(dialog);
            ApplyCurrentSize();
        }
        void OnStateChanged(object? sender, EventArgs args)
        {
            if (completed) return;
            if (dialog.Dispatcher.CheckAccess())
            {
                ApplyButtonState(dialog, state);
                return;
            }

            _ = dialog.Dispatcher.BeginInvoke(() =>
            {
                if (!completed) ApplyButtonState(dialog, state);
            });
        }

        void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            if (state is not null && !state.CanClose(MapResult(args.Result)))
            {
                args.Cancel = true;
            }
        }

        var hostSubscribed = false;
        var loadedSubscribed = false;
        var closingSubscribed = false;
        var stateSubscribed = false;

        try
        {
            host.SizeChanged += OnHostSizeChanged;
            hostSubscribed = true;
            dialog.Loaded += OnDialogLoaded;
            loadedSubscribed = true;
            dialog.Closing += OnClosing;
            closingSubscribed = true;
            if (state is not null)
            {
                state.StateChanged += OnStateChanged;
                stateSubscribed = true;
            }
            ApplyCurrentSize();

            var result = await contentDialogService.ShowAsync(dialog, cancellationToken);
            return MapResult(result);
        }
        finally
        {
            completed = true;
            try
            {
                if (hostSubscribed) host.SizeChanged -= OnHostSizeChanged;
                if (loadedSubscribed) dialog.Loaded -= OnDialogLoaded;
                if (closingSubscribed) dialog.Closing -= OnClosing;
                if (stateSubscribed) state!.StateChanged -= OnStateChanged;
            }
            finally
            {
                dialog.Content = null;
                Interlocked.Exchange(ref _isShowing, 0);
            }
        }
    }

    /// <inheritdoc/>
    public async Task ShowContentAsync(
        string title,
        object content,
        string closeButtonText = "关闭",
        CancellationToken cancellationToken = default)
    {
        _ = await ShowAsync(
            content,
            new DialogOptions(title)
            {
                CloseButtonText = closeButtonText,
                DefaultButton = DialogButton.Close
            },
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText = "确定",
        string closeButtonText = "取消",
        CancellationToken cancellationToken = default)
    {
        var result = await ShowAsync(
            message,
            new DialogOptions(title)
            {
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText,
                DefaultButton = DialogButton.Close
            },
            cancellationToken: cancellationToken);

        return result == DialogResult.Primary;
    }

    /// <inheritdoc/>
    public async Task ShowErrorAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        _ = await ShowAsync(
            message,
            new DialogOptions(title),
            cancellationToken: cancellationToken);
    }

    /// <summary>创建只使用 WPF-UI 官方 Dialog 属性的外壳。</summary>
    private static ContentDialog CreateDialog(
        object content,
        DialogOptions options,
        IDialogContentState? state)
    {
        var dialog = new ContentDialog
        {
            Title = options.Title,
            Content = content,
            PrimaryButtonText = options.PrimaryButtonText ?? string.Empty,
            SecondaryButtonText = options.SecondaryButtonText ?? string.Empty,
            CloseButtonText = options.CloseButtonText ?? string.Empty,
            DefaultButton = MapButton(options.DefaultButton)
        };
        ApplyButtonState(dialog, state);

        if (options.ScrollStrategy == DialogScrollStrategy.Content)
        {
            // WPF-UI 模板通过 TemplateBinding 把附加属性传给正文 ScrollViewer。
            // Disabled 让有限空间交给正文中的 DataGrid/ListBox，不创建第二层滚动。
            ScrollViewer.SetHorizontalScrollBarVisibility(dialog, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(dialog, ScrollBarVisibility.Disabled);
        }

        return dialog;
    }

    /// <summary>根据当前宿主的 DIP 尺寸限制 Dialog 外壳，不猜测标题和按钮区高度。</summary>
    private static void ApplyHostBounds(
        ContentDialog dialog,
        FrameworkElement? dialogShell,
        ContentDialogHost host,
        DialogOptions options)
    {
        var hostWidth = PositiveOrInfinity(host.ActualWidth);
        var hostHeight = PositiveOrInfinity(host.ActualHeight);
        var maxWidth = Math.Min(options.MaxWidth ?? double.PositiveInfinity, hostWidth);
        var maxHeight = Math.Min(options.MaxHeight ?? double.PositiveInfinity, hostHeight);

        ApplyMaximum(dialog, ContentDialog.DialogMaxWidthProperty, maxWidth);
        ApplyMaximum(dialog, ContentDialog.DialogMaxHeightProperty, maxHeight);

        var requestedWidth = ClampRequestedSize(options.Width, maxWidth);
        var requestedHeight = ClampRequestedSize(options.Height, maxHeight);

        if (requestedWidth is { } width)
            dialog.DialogWidth = width;
        else
            dialog.ClearValue(ContentDialog.DialogWidthProperty);

        if (requestedHeight is { } height)
            dialog.DialogHeight = height;
        else
            dialog.ClearValue(ContentDialog.DialogHeightProperty);

        if (dialogShell is not null)
        {
            // WPF-UI 4.3.0 将 DialogWidth/DialogHeight 作为模板外壳的最大尺寸，
            // 且会在 MeasureOverride 中按正文期望尺寸改写它们。同步最小尺寸后，
            // 调用方明确填写的尺寸才不会随列表行数变化。
            dialogShell.MinWidth = requestedWidth ?? 0;
            dialogShell.MinHeight = requestedHeight ?? 0;
        }
    }

    /// <summary>取得 WPF-UI 模板中承载标题、正文和按钮的外壳，用于落实调用方明确指定的固定尺寸。</summary>
    private static FrameworkElement? FindDialogShell(ContentDialog dialog)
    {
        dialog.ApplyTemplate();
        if (dialog.Template?.FindName("DialogRootGrid", dialog) is not Grid root || root.Children.Count == 0)
            return null;

        return root.Children[0] as FrameworkElement;
    }

    /// <summary>将首选尺寸限制在宿主与显式最大尺寸形成的有效上限内。</summary>
    private static double? ClampRequestedSize(double? requestedSize, double maximum) =>
        requestedSize is { } value ? Math.Min(value, maximum) : null;

    /// <summary>存在有限约束时写入最大尺寸，否则恢复控件或样式提供的默认值。</summary>
    private static void ApplyMaximum(ContentDialog dialog, DependencyProperty property, double value)
    {
        if (double.IsFinite(value))
            dialog.SetValue(property, value);
        else
            dialog.ClearValue(property);
    }

    /// <summary>同步状态对象提供的按钮准入；空状态使用 WPF-UI 默认启用行为。</summary>
    private static void ApplyButtonState(ContentDialog dialog, IDialogContentState? state)
    {
        dialog.IsPrimaryButtonEnabled = state?.IsPrimaryButtonEnabled ?? true;
        dialog.IsSecondaryButtonEnabled = state?.IsSecondaryButtonEnabled ?? true;
    }

    /// <summary>拒绝无法参与 WPF 布局的尺寸，避免运行时由依赖属性抛出含糊异常。</summary>
    private static void ValidateDimensions(DialogOptions options)
    {
        ValidateDimension(options.Width, nameof(options.Width));
        ValidateDimension(options.Height, nameof(options.Height));
        ValidateDimension(options.MaxWidth, nameof(options.MaxWidth));
        ValidateDimension(options.MaxHeight, nameof(options.MaxHeight));
    }

    private static void ValidateDimension(double? value, string name)
    {
        if (value is not { } dimension) return;
        if (!double.IsFinite(dimension) || dimension <= 0)
            throw new ArgumentOutOfRangeException(name, "Dialog 尺寸必须是大于零的有限 DIP 值。");
    }

    private static double PositiveOrInfinity(double value) =>
        double.IsFinite(value) && value > 0 ? value : double.PositiveInfinity;

    private static WpfContentDialogButton MapButton(DialogButton button) => button switch
    {
        DialogButton.Primary => WpfContentDialogButton.Primary,
        DialogButton.Secondary => WpfContentDialogButton.Secondary,
        DialogButton.Close => WpfContentDialogButton.Close,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "未知的 Dialog 默认按钮。")
    };

    private static DialogResult MapResult(WpfContentDialogResult result) => result switch
    {
        WpfContentDialogResult.Primary => DialogResult.Primary,
        WpfContentDialogResult.Secondary => DialogResult.Secondary,
        _ => DialogResult.Close
    };
}
