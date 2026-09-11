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

    /// <summary>根据宿主可用空间计算 Dialog 外壳尺寸，并保留模板定义的外围留白。</summary>
    private static void ApplyHostBounds(
        ContentDialog dialog,
        FrameworkElement? dialogShell,
        ContentDialogHost host,
        DialogOptions options)
    {
        var margin = dialog.DialogMargin;
        var availableWidth = GetAvailableSize(host.ActualWidth, margin.Left, margin.Right);
        var availableHeight = GetAvailableSize(host.ActualHeight, margin.Top, margin.Bottom);
        var maxWidth = GetEffectiveMaximum(options.MaxWidth, availableWidth);
        var maxHeight = GetEffectiveMaximum(options.MaxHeight, availableHeight);

        ApplyMaximum(dialog, ContentDialog.DialogMaxWidthProperty, maxWidth);
        ApplyMaximum(dialog, ContentDialog.DialogMaxHeightProperty, maxHeight);

        var requestedWidth = ResolveRequestedSize(options.Width, options.WidthRatio, availableWidth, maxWidth);
        var requestedHeight = ResolveRequestedSize(options.Height, options.HeightRatio, availableHeight, maxHeight);

        ApplyOptionalValue(dialog, ContentDialog.DialogWidthProperty, requestedWidth);
        ApplyOptionalValue(dialog, ContentDialog.DialogHeightProperty, requestedHeight);

        if (dialogShell is not null)
        {
            // WPF-UI 4.3.0 将 DialogWidth/DialogHeight 作为模板外壳的最大尺寸，
            // 且会在 MeasureOverride 中改写尺寸和对侧最大值。对实际 Border 同步
            // 最小、最大尺寸，既落实显式尺寸，也阻止内容反向撑破宿主边界。
            ApplyOptionalValue(dialogShell, FrameworkElement.MinWidthProperty, requestedWidth);
            ApplyOptionalValue(dialogShell, FrameworkElement.MinHeightProperty, requestedHeight);
            ApplyMaximum(
                dialogShell,
                FrameworkElement.MaxWidthProperty,
                requestedWidth ?? maxWidth);
            ApplyMaximum(
                dialogShell,
                FrameworkElement.MaxHeightProperty,
                requestedHeight ?? maxHeight);
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

    /// <summary>从宿主安排尺寸中扣除模板外边距；宿主尚未测量时返回空值以延后比例计算。</summary>
    private static double? GetAvailableSize(double hostSize, double leadingMargin, double trailingMargin)
    {
        if (!double.IsFinite(hostSize) || hostSize <= 0)
            return null;

        var marginSize = leadingMargin + trailingMargin;
        if (!double.IsFinite(marginSize) || marginSize < 0)
            marginSize = 0;

        return Math.Max(0, hostSize - marginSize);
    }

    /// <summary>合并调用方最大值与当前宿主上限；任一方缺失时保留另一方。</summary>
    private static double? GetEffectiveMaximum(double? requestedMaximum, double? hostMaximum) =>
        (requestedMaximum, hostMaximum) switch
        {
            ({ } requested, { } host) => Math.Min(requested, host),
            ({ } requested, null) => requested,
            (null, { } host) => host,
            _ => null
        };

    /// <summary>按固定值或宿主比例计算外壳尺寸，并限制到有效最大值。</summary>
    private static double? ResolveRequestedSize(
        double? fixedSize,
        double? ratio,
        double? availableSize,
        double? maximum)
    {
        var requestedSize = fixedSize;
        if (requestedSize is null && ratio is { } ratioValue && availableSize is { } available)
            requestedSize = available * ratioValue;

        if (requestedSize is { } value && maximum is { } limit)
            return Math.Min(value, limit);

        return requestedSize;
    }

    /// <summary>存在有限约束时写入最大尺寸，否则恢复控件或样式提供的默认值。</summary>
    private static void ApplyMaximum(DependencyObject target, DependencyProperty property, double? value)
    {
        if (value is { } maximum && double.IsFinite(maximum))
            ApplyValue(target, property, maximum);
        else
            ClearLocalValue(target, property);
    }

    /// <summary>仅在有效值变化时更新依赖属性，空值则恢复模板或样式值。</summary>
    private static void ApplyOptionalValue(
        DependencyObject target,
        DependencyProperty property,
        double? value)
    {
        if (value is { } actualValue && double.IsFinite(actualValue))
            ApplyValue(target, property, actualValue);
        else
            ClearLocalValue(target, property);
    }

    /// <summary>避免把相同尺寸重复写回 WPF 布局系统。</summary>
    private static void ApplyValue(DependencyObject target, DependencyProperty property, double value)
    {
        if (target.ReadLocalValue(property) is double current && AreClose(current, value))
            return;

        // 外壳的 MaxWidth/MaxHeight 原本是 TemplateBinding。这里需要建立本地值，
        // 避免 WPF-UI 随后的 MeasureOverride 改写 Dialog 属性后把边界再次撑大。
        target.SetValue(property, value);
    }

    /// <summary>仅清理由本服务写入的本地值，避免无意义的重新测量。</summary>
    private static void ClearLocalValue(DependencyObject target, DependencyProperty property)
    {
        if (target.ReadLocalValue(property) != DependencyProperty.UnsetValue)
            target.ClearValue(property);
    }

    private static bool AreClose(double left, double right) => Math.Abs(left - right) < 0.01;

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
        ValidateRatio(options.WidthRatio, nameof(options.WidthRatio));
        ValidateRatio(options.HeightRatio, nameof(options.HeightRatio));
        ValidateDimension(options.MaxWidth, nameof(options.MaxWidth));
        ValidateDimension(options.MaxHeight, nameof(options.MaxHeight));

        if (options.Width is not null && options.WidthRatio is not null)
            throw new ArgumentException(
                "Width 与 WidthRatio 不能同时指定。",
                nameof(DialogOptions.WidthRatio));
        if (options.Height is not null && options.HeightRatio is not null)
            throw new ArgumentException(
                "Height 与 HeightRatio 不能同时指定。",
                nameof(DialogOptions.HeightRatio));
    }

    private static void ValidateDimension(double? value, string name)
    {
        if (value is not { } dimension) return;
        if (!double.IsFinite(dimension) || dimension <= 0)
            throw new ArgumentOutOfRangeException(name, "Dialog 尺寸必须是大于零的有限 DIP 值。");
    }

    /// <summary>验证比例可安全参与宿主尺寸计算。</summary>
    private static void ValidateRatio(double? value, string name)
    {
        if (value is not { } ratio) return;
        if (!double.IsFinite(ratio) || ratio <= 0 || ratio > 1)
            throw new ArgumentOutOfRangeException(name, "Dialog 尺寸比例必须是大于零且不超过一的有限值。");
    }

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
