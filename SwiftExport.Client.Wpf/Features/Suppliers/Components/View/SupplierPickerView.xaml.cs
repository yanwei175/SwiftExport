using System.Windows.Controls;

using SwiftExportClient.Features.Suppliers.Components.Model;
using SwiftExportClient.Features.Suppliers.Components.ViewModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftExportClient.Features.Suppliers.Components.View
{
    /// <summary>
    /// SupplierPickerView.xaml 的交互逻辑
    /// </summary>
    public partial class SupplierPickerView : UserControl
    {
        /// <summary>初始化供应商选择器视图。</summary>
        public SupplierPickerView()
        {
            InitializeComponent();
        }

        /// <summary>仅将有效数据行双击转换为确认命令，避免表头、空白或滚动条误确认旧选择。</summary>
        private void SupplierGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid
                || FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row
                || row.DataContext is not SupplierPickerModel item
                || DataContext is not SupplierPickerViewModel viewModel)
                return;

            grid.SetCurrentValue(DataGrid.SelectedItemProperty, item);
            grid.GetBindingExpression(DataGrid.SelectedItemProperty)?.UpdateSource();
            if (viewModel.ConfirmCommand.CanExecute(null))
            {
                viewModel.ConfirmCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>沿当前可视树定位数据行，不缓存任何虚拟化容器。</summary>
        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source is not null)
            {
                if (source is T match) return match;
                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }
    }
}
