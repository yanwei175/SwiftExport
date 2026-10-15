using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SwiftExportClient.Features.Shipping.PackingLists.Views
{
    /// <summary>
    /// PackingListDetailView.xaml 的交互逻辑
    /// </summary>
    public partial class PackingListDetailView : UserControl
    {
        /// <summary>绑定视图级提交回调，卸载时解除，避免 ViewModel 持有已卸载视图。</summary>
        public PackingListDetailView()
        {
            InitializeComponent();
            SwiftExportClient.Resources.Behaviors.RowDragReorder.SetCommitEdits(OrderLinesGrid, CommitEditors);
            OrderLinesGrid.BeginningEdit += (_, e) =>
            {
                // 共享包装只能在组头编辑；产品数量及其他来源文本仍逐行编辑。
                if (e.Row.Item is Models.PackingListLineModel { IsMixedPacking: true }
                    && e.Column is DataGridBoundColumn { Binding: Binding binding }
                    && binding.Path?.Path is "PcsPerPackage" or "NwPerPackage" or "GwPerPackage" or "CbmPerPackage" or "Marks")
                    e.Cancel = true;
            };
            Loaded += (_, _) => AttachCommit();
            Unloaded += (_, _) => DetachCommit();
            DataContextChanged += (_, _) => { DetachCommit(); if (IsLoaded) AttachCommit(); };
        }
        private ViewModels.PackingListDetailViewModel? _owner;
        private void AttachCommit()
        {
            _owner = DataContext as ViewModels.PackingListDetailViewModel;
            if (_owner is not null) _owner.CommitEdits = CommitEditors;
        }
        private void DetachCommit() { if (_owner is not null) _owner.CommitEdits = null; _owner = null; }
        /// <summary>保存快捷键也提交仍有焦点的组头编辑器，再检查转换错误。</summary>
        private bool CommitEditors()
        {
            CommitPackingEditors(this);
            return OrderLinesGrid.CommitEdit(DataGridEditingUnit.Cell, true)
                && OrderLinesGrid.CommitEdit(DataGridEditingUnit.Row, true) && !PackingListInputValidation.HasError(this);
        }

        private static void CommitPackingEditors(DependencyObject parent)
        {
            if (parent is TextBox text && System.Windows.Automation.AutomationProperties.GetName(text).StartsWith("拼箱", StringComparison.Ordinal))
                text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                CommitPackingEditors(VisualTreeHelper.GetChild(parent, i));
        }
    }
}
