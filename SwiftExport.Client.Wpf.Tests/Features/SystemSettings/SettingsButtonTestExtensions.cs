using System.Windows;
using System.Windows.Controls.Primitives;
namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>模拟按钮的命令入口；旧事件按钮仍保留原 Click 事件路径。</summary>
internal static class SettingsButtonTestExtensions
{
    /// <summary>执行实际绑定的命令及参数，未绑定命令时触发 Click。</summary>
    internal static void InvokeClick(this ButtonBase button)
    {
        if (button.Command is { } command)
        {
            if (command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
        }
        else button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
