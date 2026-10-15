using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Features.Products.ProductList.ViewModels;

namespace SwiftExportClient.Features.Products.ProductList.Views;

/// <summary>物料详情视觉与原生表单输入桥接，不处理 API、模板来源或保存业务。</summary>
public partial class ProductDetailView : UserControl
{
    private ProductDetailViewModel? _attached;
    private bool _changingTab;

    /// <summary>初始化物料详情视图。</summary>
    public ProductDetailView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach(DataContext as ProductDetailViewModel);
    private void OnUnloaded(object sender, RoutedEventArgs e) => Attach(null);
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (IsLoaded) Attach(e.NewValue as ProductDetailViewModel); }

    /// <summary>只在视图活跃时提供编辑桥接，卸载或更换模型后解除旧页面引用。</summary>
    private void Attach(ProductDetailViewModel? viewModel)
    {
        if (ReferenceEquals(_attached, viewModel)) return;
        if (_attached is not null)
        { _attached.CommitPendingEdits = null; _attached.RefreshEditorTargets = null; }
        _attached = viewModel;
        if (_attached is not null)
        { _attached.CommitPendingEdits = CommitEdits; _attached.RefreshEditorTargets = RestoreEditorTargets; }
    }

    /// <summary>提交失焦前的文本缓冲；原生转换错误留在控件内，阻止采集旧数值保存。</summary>
    private bool CommitEdits()
    {
        Keyboard.ClearFocus();
        var valid = true;
        foreach (var textBox in Descendants(EditableFields).OfType<TextBox>())
        {
            var expression = textBox.GetBindingExpression(TextBox.TextProperty);
            if (expression?.ParentBinding.Mode is not BindingMode.OneWay and not BindingMode.OneTime)
                expression?.UpdateSource();
            valid &= !Validation.GetHasError(textBox);
        }
        return valid && (_attached?.Packaging.CommitPendingEdits?.Invoke() ?? true);
    }

    /// <summary>两个面板保留各自视觉树；切换前提交输入，转换错误则恢复原页签，不丢失编辑缓冲。</summary>
    private void OnDetailTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingTab || !ReferenceEquals(e.OriginalSource, sender) || e.RemovedItems.Count == 0) return;
        _changingTab = true;
        try { if (!CommitEdits()) DetailTabs.SelectedItem = e.RemovedItems[0]; }
        finally { _changingTab = false; }
    }

    /// <summary>放弃或成功重载后恢复目标文本，覆盖转换失败而尚未写入模型的原生缓冲。</summary>
    private void RestoreEditorTargets()
    {
        foreach (var textBox in Descendants(EditableFields).OfType<TextBox>())
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    /// <summary>在已加载视觉树内找到原生输入控件，避免把绑定或业务状态移动到视图。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
