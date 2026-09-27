using System.Windows.Controls;
using System.Windows;
using Microsoft.Win32;
using SwiftExportClient.Features.Products.ProductList.ViewModels;

namespace SwiftExportClient.Features.Products.ProductList.Views;

/// <summary>展示图片上传队列和服务端处理进度；状态与操作归 ViewModel。</summary>
public partial class ProductImageUploadView : UserControl
{
    /// <summary>初始化上传弹窗的视觉内容。</summary>
    public ProductImageUploadView() => InitializeComponent();

    /// <summary>仅桥接原生文件选择器；取消保留当前队列，确认后由 ViewModel 接纳选择。</summary>
    private void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductImageUploadViewModel model || !model.CanSelectFiles) return;
        var picker = new OpenFileDialog
        {
            Title = "选择产品主图（文件名将作为服务器图片名）",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "产品图片 (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp"
        };
        var owner = Window.GetWindow(this);
        var selected = owner is null ? picker.ShowDialog() : picker.ShowDialog(owner);
        if (selected == true && model.CanSelectFiles) model.Prepare(picker.FileNames);
    }
}
