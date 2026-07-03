using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.ComponentModel;
using System.Linq;

namespace MateWpfThemes.FW.Controls
{
    /// <summary>
    /// ERP 明细录入常用 DataGrid 键盘行为轻封装。
    /// </summary>
    public class EditableDataGrid : DataGrid
    {
        public static readonly DependencyProperty AddNewRowCommandProperty =
            DependencyProperty.Register(nameof(AddNewRowCommand), typeof(ICommand), typeof(EditableDataGrid), new PropertyMetadata(null));

        public static readonly DependencyProperty DeleteRowCommandProperty =
            DependencyProperty.Register(nameof(DeleteRowCommand), typeof(ICommand), typeof(EditableDataGrid), new PropertyMetadata(null));

        public static readonly DependencyProperty CellValueCommittedCommandProperty =
            DependencyProperty.Register(nameof(CellValueCommittedCommand), typeof(ICommand), typeof(EditableDataGrid), new PropertyMetadata(null));

        public static readonly DependencyProperty PasteRowsCommandProperty =
            DependencyProperty.Register(nameof(PasteRowsCommand), typeof(ICommand), typeof(EditableDataGrid), new PropertyMetadata(null));

        public static readonly DependencyProperty EnterNavigationModeProperty =
            DependencyProperty.Register(nameof(EnterNavigationMode), typeof(EnterNavigationMode), typeof(EditableDataGrid), new PropertyMetadata(EnterNavigationMode.NextCell));

        public static readonly DependencyProperty AutoAppendNewRowProperty =
            DependencyProperty.Register(nameof(AutoAppendNewRow), typeof(bool), typeof(EditableDataGrid), new PropertyMetadata(false));

        public ICommand AddNewRowCommand
        {
            get => (ICommand)GetValue(AddNewRowCommandProperty);
            set => SetValue(AddNewRowCommandProperty, value);
        }

        public ICommand DeleteRowCommand
        {
            get => (ICommand)GetValue(DeleteRowCommandProperty);
            set => SetValue(DeleteRowCommandProperty, value);
        }

        public ICommand CellValueCommittedCommand
        {
            get => (ICommand)GetValue(CellValueCommittedCommandProperty);
            set => SetValue(CellValueCommittedCommandProperty, value);
        }

        public ICommand PasteRowsCommand
        {
            get => (ICommand)GetValue(PasteRowsCommandProperty);
            set => SetValue(PasteRowsCommandProperty, value);
        }

        public EnterNavigationMode EnterNavigationMode
        {
            get => (EnterNavigationMode)GetValue(EnterNavigationModeProperty);
            set => SetValue(EnterNavigationModeProperty, value);
        }

        public bool AutoAppendNewRow
        {
            get => (bool)GetValue(AutoAppendNewRowProperty);
            set => SetValue(AutoAppendNewRowProperty, value);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && EnterNavigationMode != EnterNavigationMode.None)
            {
                CommitEdit(DataGridEditingUnit.Cell, true);
                CommitEdit(DataGridEditingUnit.Row, true);
                ExecuteCommand(CellValueCommittedCommand, CurrentItem);
                AppendNewRowIfNeeded();
                MoveAfterEnter();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete && DeleteRowCommand != null)
            {
                if (ExecuteCommand(DeleteRowCommand, SelectedItem))
                {
                    e.Handled = true;
                    return;
                }
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V && PasteRowsCommand != null)
            {
                if (ExecuteCommand(PasteRowsCommand, Clipboard.GetText()))
                {
                    e.Handled = true;
                    return;
                }
            }

            base.OnPreviewKeyDown(e);
        }

        private void MoveAfterEnter()
        {
            if (EnterNavigationMode == EnterNavigationMode.NextRow)
            {
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
                return;
            }

            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void AppendNewRowIfNeeded()
        {
            if (!AutoAppendNewRow || AddNewRowCommand == null || !IsCurrentCellLastEditableCell())
            {
                return;
            }

            ExecuteCommand(AddNewRowCommand, CurrentItem);
        }

        private bool IsCurrentCellLastEditableCell()
        {
            if (IsReadOnly || CurrentItem == null || CurrentItem == CollectionView.NewItemPlaceholder || CurrentColumn == null)
            {
                return false;
            }

            var lastItem = GetLastDataItem();
            if (!Equals(CurrentItem, lastItem))
            {
                return false;
            }

            var lastEditableColumn = Columns
                .Where(x => x.Visibility == Visibility.Visible && !x.IsReadOnly)
                .OrderBy(x => x.DisplayIndex)
                .LastOrDefault();

            return lastEditableColumn != null && CurrentColumn.DisplayIndex == lastEditableColumn.DisplayIndex;
        }

        private object GetLastDataItem()
        {
            object lastItem = null;
            foreach (var item in Items)
            {
                if (item == null || item == CollectionView.NewItemPlaceholder)
                {
                    continue;
                }

                lastItem = item;
            }

            return lastItem;
        }

        private static bool ExecuteCommand(ICommand command, object parameter)
        {
            if (command == null || !command.CanExecute(parameter))
            {
                return false;
            }

            command.Execute(parameter);
            return true;
        }
    }
}
