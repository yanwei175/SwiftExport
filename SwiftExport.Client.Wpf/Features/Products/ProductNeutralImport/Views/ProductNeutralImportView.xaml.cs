using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SwiftExportClient.Features.Products.ProductNeutralImport.Services;
using SwiftExportClient.Features.Products.ProductNeutralImport.ViewModels;

namespace SwiftExportClient.Features.Products.ProductNeutralImport.Views;

/// <summary>仅桥接系统文件对话框，解析、校验和队列交给 ViewModel。</summary>
public partial class ProductNeutralImportView : UserControl
{
    /// <summary>创建只读导入队列视图。</summary>
    public ProductNeutralImportView() => InitializeComponent();
    private async void OnChooseFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductNeutralImportViewModel vm || !vm.LoadFileCommand.CanExecute(string.Empty)) return;
        var picker = new OpenFileDialog { Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await vm.LoadFileCommand.ExecuteAsync(picker.FileName);
    }
    private async void OnDownloadTemplate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductNeutralImportViewModel vm || !vm.DownloadTemplateCommand.CanExecute(null)) return;
        var target = SaveTarget("中性产品导入模板.xlsx");
        if (target is not null) await vm.DownloadTemplateCommand.ExecuteAsync(target);
    }
    private async void OnDownloadErrors(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductNeutralImportViewModel vm || !vm.DownloadErrorsCommand.CanExecute(null)) return;
        var target = SaveTarget("中性产品待修改.xlsx");
        if (target is not null) await vm.DownloadErrorsCommand.ExecuteAsync(target);
    }
    /// <summary>覆盖许可只来自系统保存对话框明确确认，最终仍按原子发布规则保存。</summary>
    private ProductNeutralImportFileTarget? SaveTarget(string name)
    {
        var picker = new SaveFileDialog { Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", FileName = name, DefaultExt = ".xlsx", AddExtension = true, OverwritePrompt = true };
        return picker.ShowDialog(Window.GetWindow(this)) == true ? new(picker.FileName, File.Exists(picker.FileName)) : null;
    }
}
