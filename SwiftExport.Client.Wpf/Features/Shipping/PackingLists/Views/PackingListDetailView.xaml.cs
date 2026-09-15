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
        private bool CommitEditors() => OrderLinesGrid.CommitEdit(DataGridEditingUnit.Cell, true)
            && OrderLinesGrid.CommitEdit(DataGridEditingUnit.Row, true) && !PackingListInputValidation.HasError(this);
    }
}
