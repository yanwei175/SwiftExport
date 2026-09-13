namespace SwiftExportClient.Services.UI;

/// <summary>为需要从内容区主动触发公共弹窗按钮结果的可选状态对象提供最小关闭请求协议。</summary>
public interface IDialogCloseRequest
{
    /// <summary>内容区请求按指定按钮结果关闭时触发；公共弹窗服务会在结束时解除订阅。</summary>
    event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;
}

/// <summary>描述内容区请求的公共弹窗结果。</summary>
public sealed class DialogCloseRequestedEventArgs : EventArgs
{
    /// <summary>创建指定结果的关闭请求。</summary>
    /// <param name="result">应与公共外壳按钮语义一致的结果。</param>
    public DialogCloseRequestedEventArgs(DialogResult result) => Result = result;

    /// <summary>获取请求的弹窗结果。</summary>
    public DialogResult Result { get; }
}
