using SwiftExportClient.Features.Products.ProductList.Models;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.Products.ProductList.Services;

/// <summary>通过应用 ContentDialog 宿主提供主型号同步确认；文案和控件只归本功能所有。</summary>
public sealed class ProductMasterSyncDialogService(IContentDialogService contentDialogService) : IProductMasterSyncDialogService
{
    /// <summary>每次保存重新收集两组物料覆盖选项；取消时返回未确认结果，使调用方停止提交。</summary>
    public async Task<ProductMasterSyncDialogResult> ConfirmSaveAsync(CancellationToken cancellationToken = default)
    {
        // 每次新建控件且默认不勾选，防止重试或另一文档继承覆盖授权。
        var descriptions = new CheckBox { Content = "更新描述配置", IsChecked = false, Margin = new Thickness(0, 16, 0, 8) };
        var supplier = new CheckBox { Content = "更新默认供应商", IsChecked = false };

        AutomationProperties.SetName(descriptions, "更新描述配置");
        AutomationProperties.SetName(supplier, "更新默认供应商");
        var content = new StackPanel { MaxWidth = 480 };

        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "确认后保存主型号及本次变更，并同步关联物料的主型号编码。勾选的资料组将覆盖所有未删除关联物料的当前值（包括停用物料和空值清除），不影响历史单据。两项都不勾选时，仅同步型号编码。取消则不保存。",
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(descriptions);
        content.Children.Add(supplier);
        var result = await contentDialogService.ShowAsync(new ContentDialog
        {
            Title = "保存主型号 · 同步关联物料",
            Content = content,
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        }, cancellationToken);

        return result == ContentDialogResult.Primary
            ? new ProductMasterSyncDialogResult
            {
                Confirmed = true,
                UpdateProductDescriptions = descriptions.IsChecked == true,
                UpdateProductDefaultSupplier = supplier.IsChecked == true
            }
            : new ProductMasterSyncDialogResult();
    }
}
