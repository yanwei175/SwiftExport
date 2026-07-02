using MateWpfThemes.FW.Mvvm;
using System;

namespace MateWpfThemes.FW.Services.Dialogs
{
    // 1. 内部 UI 枚举
    public enum DialogIcon
    {
        Info,
        Warning,
        Error,
        Question
    }
    // 确认框的 ViewModel
    public class MsgBoxViewModel
    {
        public string Message { get; set; }
        public string Title { get; set; }

        public DialogIcon Icon { get; set; }

        // 控制是否显示取消按钮 (Info/Error 只有确定，Question 有确定和取消)
        public bool ShowCancelButton { get; set; }

        // 确定按钮的文字 (支持自定义为 "确定"、"重试"、"我知道了" 等)
        public string ConfirmText { get; set; } = "确定";
        public Action OnConfirm { get; set; }
        public Action OnCancel { get; set; }

        public RelayCommand ConfirmCommand => new RelayCommand(() => OnConfirm?.Invoke());
        public RelayCommand CancelCommand => new RelayCommand(() => OnCancel?.Invoke());
    }

    // 输入框的 ViewModel
    public class InputBoxViewModel
    {
        public string Message { get; set; }
        public string Title { get; set; }
        public string InputText { get; set; }
        public Action<string> OnSubmit { get; set; }
        public Action OnCancel { get; set; }

        public RelayCommand SubmitCommand => new RelayCommand(() => OnSubmit?.Invoke(InputText));
        public RelayCommand CancelCommand => new RelayCommand(() => OnCancel?.Invoke());
    }
}