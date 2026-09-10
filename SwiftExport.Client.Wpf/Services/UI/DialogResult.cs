namespace SwiftExportClient.Services.UI;

/// <summary>表示用户通过哪个按钮结束弹窗。</summary>
public enum DialogResult
{
    /// <summary>用户选择主按钮。</summary>
    Primary,
    /// <summary>用户选择次按钮。</summary>
    Secondary,
    /// <summary>用户选择关闭按钮、按 Esc 或以其他正常方式关闭。</summary>
    Close
}

/// <summary>指定弹窗获得初始焦点并由 Enter 触发的默认按钮。</summary>
public enum DialogButton
{
    /// <summary>主按钮。</summary>
    Primary,
    /// <summary>次按钮。</summary>
    Secondary,
    /// <summary>关闭按钮。</summary>
    Close
}

/// <summary>指定弹窗正文的滚动所有权。</summary>
public enum DialogScrollStrategy
{
    /// <summary>保留 WPF-UI ContentDialog 的默认外层滚动行为。</summary>
    Dialog,
    /// <summary>禁用 ContentDialog 外层滚动，由正文中的 DataGrid、ListBox 等控件负责滚动。</summary>
    Content
}
