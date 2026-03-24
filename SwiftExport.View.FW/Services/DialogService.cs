using MateWpfThemes.FW.Services.Dialogs;
using SwiftExport.Contracts.UiInterface;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Services
{
    public class DialogService : IDialogService
    {
        public Task ShowInfoAsync(string message, string title = "提示")
        {
            return DialogProvider.ShowMessageAsync(message, title, DialogIcon.Info);
        }

        public Task ShowWarningAsync(string message, string title = "警告")
        {
            return DialogProvider.ShowMessageAsync(message, title, DialogIcon.Warning);
        }

        public Task ShowErrorAsync(string message, string title = "错误")
        {
            return DialogProvider.ShowMessageAsync(message, title, DialogIcon.Error);
        }

        public Task<bool> ShowConfirmAsync(string message, string title = "确认")
        {
            return DialogProvider.ShowConfirmAsync(message, title);
        }

        public Task<string> ShowInputAsync(string message, string title = "请输入")
        {
            return DialogProvider.ShowInputAsync(message, title);
        }
    }
}
