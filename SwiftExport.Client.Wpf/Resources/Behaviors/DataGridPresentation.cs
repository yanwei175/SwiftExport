using System.Windows;
using System.Windows.Controls;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>为表格样式提供可继承的宿主引用；直接读取原生状态，不维护选择镜像或订阅事件。</summary>
public static class DataGridPresentation
{
    /// <summary>由表格样式设置为自身，供行和单元格在挂载期间读取原生 CurrentCell。</summary>
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner", typeof(DataGrid), typeof(DataGridPresentation),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>获取样式所属表格；尚未挂载的容器返回空。</summary>
    public static DataGrid? GetOwner(DependencyObject element) => (DataGrid?)element.GetValue(OwnerProperty);

    /// <summary>设置可由表格容器继承的宿主引用。</summary>
    public static void SetOwner(DependencyObject element, DataGrid? value) => element.SetValue(OwnerProperty, value);
}
