using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.Forms.ViewModels;

/// <summary>可复用的表单关闭状态：宿主决定如何关闭，表单仅表达意图与草稿保护。</summary>
public sealed partial class SettingsFormState(Func<bool> hasDraft, Func<bool> isBusy, Action discard)
    : ObservableObject, IDialogContentState, IDialogCloseRequest
{
    private bool _completed, _forced;
    /// <summary>放弃确认显示状态；确认始终位于当前内容内。</summary>
    [ObservableProperty]
    public partial bool IsDiscardPromptVisible { get; private set; }
    /// <summary>表单已被撤权或主动放弃，不再接纳保存结果。</summary>
    public bool WasForced => _forced;
    /// <inheritdoc />
    public event EventHandler? StateChanged;
    /// <inheritdoc />
    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;
    /// <inheritdoc />
    public bool IsPrimaryButtonEnabled => _completed || (!_forced && !isBusy());
    /// <inheritdoc />
    public bool IsSecondaryButtonEnabled => !isBusy();
    /// <summary>通知宿主重算按钮准入。</summary>
    public void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
    /// <summary>所有关闭入口共用同步检查，不在此执行保存或弹出嵌套 Dialog。</summary>
    public bool CanClose(DialogResult result)
    {
        if (_forced || _completed) return true;
        if (isBusy()) return false;
        if (!hasDraft()) return true;
        IsDiscardPromptVisible = true;
        return false;
    }
    /// <summary>向任意宿主请求关闭；宿主无需了解具体表单。</summary>
    [RelayCommand]
    private void Close()
    {
        if (CanClose(DialogResult.Close)) CloseRequested?.Invoke(this, new(DialogResult.Close));
    }
    /// <summary>取消放弃，保留当前编辑。</summary>
    [RelayCommand]
    private void KeepEditing() => IsDiscardPromptVisible = false;
    /// <summary>确认放弃后清理草稿并请求关闭。</summary>
    [RelayCommand]
    private void Discard()
    {
        if (!isBusy()) ForceClose();
    }
    /// <summary>明确保存成功才允许主操作结束表单。</summary>
    public void Complete()
    {
        if (_forced) return;
        _completed = true;
        CloseRequested?.Invoke(this, new(DialogResult.Primary));
    }
    /// <summary>会话失效时立即清理并绕过普通关闭保护。</summary>
    public void ForceClose()
    {
        if (_forced) return;
        _forced = true;
        discard();
        CloseRequested?.Invoke(this, new(DialogResult.Close));
    }
}
