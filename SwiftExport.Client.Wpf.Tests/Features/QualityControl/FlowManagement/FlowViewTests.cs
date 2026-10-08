using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExportClient.Features.QualityControl.FlowManagement.Models;
using SwiftExportClient.Features.QualityControl.FlowManagement.Views;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.FlowManagement;

/// <summary>真实 STA 视图验证，使用离线模型且窗口不可见，不操作运行数据库。</summary>
[TestClass]
[DoNotParallelize]
public sealed class FlowViewTests
{
    /// <summary>实际表格数字输入遵循当前文化，格式错误不能保存旧值，权限撤回取消原生编辑。</summary>
    [TestMethod]
    public void ActualGridBindingsHandleCultureInvalidTextAndReadOnly()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var previous = CultureInfo.CurrentCulture;
            try
            {
                foreach (var culture in new[] { "zh-CN", "de-DE" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    var host = new FlowTestHost(); using var vm = host.Editor();
                    await vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
                    var view = new FlowDetailView { DataContext = vm, Background = SystemColors.WindowBrush };
                    var window = new Window { Content = view, Width = 1500, Height = 900, Opacity = 0,
                        ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                    try
                    {
                        window.Show(); await Pump();
                        if (culture == "zh-CN") SavePreview(view, "FlowDetail.png");
                        var grid = (DataGrid)view.FindName("FlowGrid");
                        var line = vm.Lines[0]; grid.SelectedItem = line;
                        grid.CurrentCell = new DataGridCellInfo(line, grid.Columns[3]);
                        grid.ScrollIntoView(line, grid.Columns[3]); grid.Focus();
                        Assert.IsTrue(grid.BeginEdit()); await Pump();
                        var editor = grid.Columns[3].GetCellContent(line) as TextBox;
                        Assert.IsNotNull(editor);
                        editor.Text = "invalid"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); await Pump();
                        Assert.IsTrue(vm.HasInputErrors); Assert.IsTrue(vm.HasUnsavedChanges); Assert.IsFalse(vm.IsDirty);
                        host.Dialogs.Confirm = false;
                        Assert.IsFalse(await vm.CanCloseAsync(default)); host.Dialogs.Confirm = true;
                        editor.Text = culture == "de-DE" ? "0,5" : "0.5";
                        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); await Pump();
                        Assert.AreEqual(0.5m, line.Flow1BarLMin);
                        editor.Text = "invalid"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); await Pump();
                        Assert.IsTrue(Validation.GetHasError(editor)); Assert.AreEqual(0.5m, line.Flow1BarLMin);
                        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, host.Api.SaveCalls);
                        editor.Text = ""; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); await Pump();
                        Assert.IsNull(line.Flow1BarLMin);
                        editor.Text = "0"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); await Pump();
                        Assert.AreEqual(0m, line.Flow1BarLMin);
                        Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
                        var datePicker = (DatePicker)view.FindName("TestDatePicker");
                        var validDateText = datePicker.Text;
                        datePicker.Text = "invalid";
                        await vm.SaveCommand.ExecuteAsync(null);
                        Assert.AreEqual(0, host.Api.SaveCalls);
                        datePicker.Text = validDateText; await Pump();
                        await vm.SaveCommand.ExecuteAsync(null);
                        var dateText = ((DatePicker)view.FindName("TestDatePicker")).Text;
                        Assert.AreEqual(1, host.Api.SaveCalls, $"culture={culture}; inputErrors={vm.HasInputErrors}; dirty={vm.IsDirty}; summary={vm.ValidationSummary}; date={dateText}");
                        Assert.IsFalse(vm.IsDirty);
                        host.User.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.QualityControlFlowView] });
                        await Pump();
                        grid.SelectedItem = vm.Lines[0]; grid.CurrentCell = new DataGridCellInfo(vm.Lines[0], grid.Columns[3]);
                        Assert.IsFalse(grid.BeginEdit()); Assert.IsFalse(vm.CanEdit);
                        Assert.IsFalse(((Control)view.FindName("MaterialEditor")).IsEnabled);
                    }
                    finally { window.Close(); await Pump(); Assert.IsNull(vm.CommitPendingEdits); }
                    using var manager = host.Manager(); await manager.InitializeAsync();
                    var managerView = new FlowManagerView { DataContext = manager, Background = SystemColors.WindowBrush };
                    var listWindow = new Window { Content = managerView, Width = 1500, Height = 900, Opacity = 0,
                        ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                    try
                    {
                        listWindow.Show(); await Pump();
                        if (culture == "zh-CN") SavePreview(managerView, "FlowList.png");
                    }
                    finally { listWindow.Close(); await Pump(); }
                }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
    }
    /// <summary>处理实际 Dispatcher 的布局、绑定和 Loaded／Unloaded，不依靠源码文本证明交互。</summary>
    private static async Task Pump()
    {
        // 复用常驻 STA Application，以 Background 等待绑定和布局，不创建第二个全局宿主。
        await Dispatcher.Yield(DispatcherPriority.Background);
    }
    /// <summary>保存真实视图的离线预览作为人工检查素材，不截取用户桌面或连接数据库。</summary>
    private static void SavePreview(FrameworkElement view, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../TestResults/FlowManagement"));
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
}

