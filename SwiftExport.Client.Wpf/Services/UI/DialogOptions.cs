namespace SwiftExportClient.Services.UI;

/// <summary>描述公共内容弹窗的标题、按钮、尺寸和滚动策略。</summary>
public sealed class DialogOptions
{
    /// <summary>创建具有必填标题的弹窗选项。</summary>
    /// <param name="title">向用户显示的弹窗标题。</param>
    public DialogOptions(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    /// <summary>获取弹窗标题。</summary>
    public string Title { get; }
    /// <summary>获取主按钮文字；空值或空字符串表示不显示。</summary>
    public string? PrimaryButtonText { get; init; }
    /// <summary>获取次按钮文字；空值或空字符串表示不显示。</summary>
    public string? SecondaryButtonText { get; init; }
    /// <summary>获取关闭按钮文字；空值或空字符串表示不显示。</summary>
    public string? CloseButtonText { get; init; } = "关闭";
    /// <summary>获取默认按钮。</summary>
    public DialogButton DefaultButton { get; init; } = DialogButton.Close;
    /// <summary>获取固定外壳宽度；空值保留 WPF-UI 的按内容测量行为。</summary>
    public double? Width { get; init; }
    /// <summary>获取固定外壳高度；空值保留 WPF-UI 的按内容测量行为。</summary>
    public double? Height { get; init; }
    /// <summary>获取外壳占宿主可用宽度的比例；有效范围为大于零且不超过一，不能与 Width 同时设置。</summary>
    public double? WidthRatio { get; init; }
    /// <summary>获取外壳占宿主可用高度的比例；有效范围为大于零且不超过一，不能与 Height 同时设置。</summary>
    public double? HeightRatio { get; init; }
    /// <summary>获取外壳最大宽度；最终值还会受当前宿主宽度限制。</summary>
    public double? MaxWidth { get; init; }
    /// <summary>获取外壳最大高度；最终值还会受当前宿主高度限制。</summary>
    public double? MaxHeight { get; init; }
    /// <summary>获取正文滚动策略。</summary>
    public DialogScrollStrategy ScrollStrategy { get; init; } = DialogScrollStrategy.Dialog;
}
