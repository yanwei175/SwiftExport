using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwiftExportClient.Features.Shipping.PackingLists.Views;

/// <summary>仅检查当前视图的 WPF 转换错误，防止无效文本被旧模型值替代后提交。</summary>
internal static class PackingListInputValidation
{
    /// <summary>检查已实例化编辑器；虚拟化外的业务值由模型校验负责。</summary>
    internal static bool HasError(DependencyObject root)
    {
        if (Validation.GetHasError(root)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (HasError(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }
}
