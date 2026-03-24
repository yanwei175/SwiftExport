using System.Threading.Tasks;

namespace SwiftExport.Contracts.UiInterface
{
    public interface IDialogService
    {
        Task ShowInfoAsync(string message, string title = "提示");
        Task ShowWarningAsync(string message, string title = "警告");
        Task ShowErrorAsync(string message, string title = "错误");

        Task<bool> ShowConfirmAsync(string message, string title = "确认");

        // 对于输入框，message 通常是提示语（比如 "请输入验证码"）
        Task<string> ShowInputAsync(string message, string title = "请输入");
    }
}
