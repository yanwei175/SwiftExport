namespace SwiftExportClient.Services.UI;

/// <summary>向公共弹窗暴露按钮状态和同步关闭校验，不持有 View，也不负责业务对象的打开或释放。</summary>
public interface IDialogContentState
{
    /// <summary>按钮准入状态变化时触发；弹窗服务会在结束时解除订阅。</summary>
    event EventHandler? StateChanged;
    /// <summary>获取主按钮当前是否可用。</summary>
    bool IsPrimaryButtonEnabled { get; }
    /// <summary>获取次按钮当前是否可用。</summary>
    bool IsSecondaryButtonEnabled { get; }
    /// <summary>判断显式按钮或 Esc 请求是否允许关闭。该方法必须同步、快速且不能启动另一个弹窗。</summary>
    /// <param name="requestedResult">本次关闭请求对应的按钮结果。</param>
    /// <returns>允许关闭时返回 <see langword="true"/>。</returns>
    bool CanClose(DialogResult requestedResult);
}
