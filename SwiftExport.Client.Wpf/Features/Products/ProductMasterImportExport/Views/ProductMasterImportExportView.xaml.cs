using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
using SwiftExportClient.Features.Products.ProductMasterImportExport.ViewModels;
namespace SwiftExportClient.Features.Products.ProductMasterImportExport.Views;
/// <summary>仅桥接文件对话框和 DataGrid 编辑事务，不承担业务解析或 API 调用。</summary>
public partial class ProductMasterImportExportView : UserControl
{
    private ProductMasterImportExportViewModel? _attached;
    /// <summary>初始化父级导入视图。</summary>
    public ProductMasterImportExportView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) Attach(); }
    /// <summary>真实编辑事务必须在冻结请求或关闭前完成。</summary>
    private void Attach()
    { Detach(); if (DataContext is ProductMasterImportExportViewModel vm) { _attached = vm; vm.CommitPendingEdits = CommitEdits; } }
    private void Detach() { if (_attached is not null) _attached.CommitPendingEdits = null; _attached = null; }
    private bool CommitEdits() => ImportGrid.CommitEdit(DataGridEditingUnit.Cell,true) && ImportGrid.CommitEdit(DataGridEditingUnit.Row,true);
    private async void OnChooseFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductMasterImportExportViewModel vm || !vm.LoadFileCommand.CanExecute(string.Empty)) return;
        var picker = new OpenFileDialog { Filter="Excel 工作簿 (*.xlsx)|*.xlsx", Multiselect=false };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await vm.LoadFileCommand.ExecuteAsync(picker.FileName);
    }
    private async void OnDownloadTemplate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductMasterImportExportViewModel vm || !vm.DownloadTemplateCommand.CanExecute(null)) return;
        var target = SaveTarget("父级型号导入模板.xlsx");
        if (target is not null) await vm.DownloadTemplateCommand.ExecuteAsync(target);
    }
    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductMasterImportExportViewModel vm || !vm.ExportCommand.CanExecute(null)) return;
        var target = SaveTarget("父级型号导出.xlsx");
        if (target is not null) await vm.ExportCommand.ExecuteAsync(target);
    }
    /// <summary>覆盖许可只来自系统保存对话框明确确认；后台发布仍拒绝未获许可的覆盖。</summary>
    private ProductMasterFileTarget? SaveTarget(string name)
    {
        var picker = new SaveFileDialog { Filter="Excel 工作簿 (*.xlsx)|*.xlsx",FileName=name,DefaultExt=".xlsx",AddExtension=true,OverwritePrompt=true };
        return picker.ShowDialog(Window.GetWindow(this)) == true ? new(picker.FileName,File.Exists(picker.FileName)) : null;
    }
}
