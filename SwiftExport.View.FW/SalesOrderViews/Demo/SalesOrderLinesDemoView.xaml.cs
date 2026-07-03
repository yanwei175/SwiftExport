using MateWpfThemes.FW.Controls;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.SalesOrderViews.Demo
{
    public partial class SalesOrderLinesDemoView : UserControl
    {
        public SalesOrderLinesDemoView()
        {
            InitializeComponent();
        }

        public SalesOrderLinesDemoView(SalesOrderLinesDemoViewModel vm) : this()
        {
            DataContext = vm;
        }

        private void ProductLookupEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is SearchableTextBoxPlus editor)
            {
                editor.FocusEditor();
            }
        }

        private void ProductLookupEditor_SelectionCommitted(object sender, LookupSelectionCommittedEventArgs e)
        {
            if (!(DataContext is SalesOrderLinesDemoViewModel vm) ||
                !(sender is FrameworkElement editor))
            {
                return;
            }

            var context = new LookupCommitContext
            {
                RowItem = editor.DataContext,
                SelectedItem = e.SelectedItem,
                SelectedValue = e.SelectedValue,
                SearchText = e.SearchText,
                ColumnKey = "Product",
                CommitReason = e.CommitReason
            };

            if (vm.LookupCommittedCommand.CanExecute(context))
            {
                vm.LookupCommittedCommand.Execute(context);
            }
        }
    }
}
