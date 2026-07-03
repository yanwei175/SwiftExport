using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MateWpfThemes.FW.Controls
{
    /// <summary>
    /// DataGrid 通用搜索选择列，编辑器基于 SearchableTextBoxPlus。
    /// </summary>
    public class DataGridSearchableColumn : DataGridColumn
    {
        private static readonly DependencyProperty ResolvedSelectionCommittedCommandProperty =
            DependencyProperty.RegisterAttached(
                "ResolvedSelectionCommittedCommand",
                typeof(ICommand),
                typeof(DataGridSearchableColumn),
                new PropertyMetadata(null));

        public string ColumnKey { get; set; } = string.Empty;
        public BindingBase DisplayBinding { get; set; }
        public BindingBase SearchTextBinding { get; set; }
        public BindingBase ItemsSourceBinding { get; set; }
        public BindingBase SelectedItemBinding { get; set; }
        public BindingBase SelectedValueBinding { get; set; }
        public BindingBase SelectionCommittedCommand { get; set; }
        public string SelectedValuePath { get; set; } = string.Empty;
        public string DisplayMemberPath { get; set; } = string.Empty;
        public string FilterMemberPaths { get; set; } = string.Empty;
        public DataTemplate ItemTemplate { get; set; }
        public CommitParameterMode CommitParameterMode { get; set; } = CommitParameterMode.LookupCommitContext;

        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
        {
            var textBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            ApplyBinding(textBlock, TextBlock.TextProperty, DisplayBinding);
            return textBlock;
        }

        protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
        {
            var editor = new SearchableTextBoxPlus
            {
                DisplayMemberPath = DisplayMemberPath ?? string.Empty,
                FilterMemberPaths = FilterMemberPaths ?? string.Empty,
                SelectedValuePath = SelectedValuePath ?? string.Empty,
                ItemTemplate = ItemTemplate,
                SearchMode = SearchableTextBoxSearchMode.Auto,
                MinHeight = 28
            };

            ApplyBinding(editor, SearchableTextBoxPlus.TextProperty, SearchTextBinding);
            ApplyBinding(editor, SearchableTextBoxPlus.ItemsSourceProperty, ItemsSourceBinding);
            ApplyBinding(editor, SearchableTextBoxPlus.SelectedItemProperty, SelectedItemBinding);
            ApplyBinding(editor, SearchableTextBoxPlus.SelectedValueProperty, SelectedValueBinding);
            ApplyBinding(editor, ResolvedSelectionCommittedCommandProperty, SelectionCommittedCommand);

            editor.SelectionCommitted += (sender, args) =>
            {
                var command = (ICommand)editor.GetValue(ResolvedSelectionCommittedCommandProperty);
                if (command == null)
                {
                    return;
                }

                var context = new LookupCommitContext
                {
                    RowItem = dataItem,
                    SelectedItem = args.SelectedItem,
                    SelectedValue = args.SelectedValue,
                    SearchText = args.SearchText,
                    ColumnKey = ColumnKey ?? string.Empty,
                    CommitReason = args.CommitReason
                };

                var parameter = BuildCommandParameter(context);
                if (command.CanExecute(parameter))
                {
                    command.Execute(parameter);
                }
            };

            editor.Loaded += (sender, args) =>
            {
                editor.Focus();
                Keyboard.Focus(editor);
            };

            return editor;
        }

        protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
        {
            editingElement?.Focus();
            return null;
        }

        private object BuildCommandParameter(LookupCommitContext context)
        {
            switch (CommitParameterMode)
            {
                case CommitParameterMode.SelectedItem:
                    return context.SelectedItem;
                case CommitParameterMode.SelectedValue:
                    return context.SelectedValue;
                case CommitParameterMode.RowItem:
                    return context.RowItem;
                case CommitParameterMode.RowAndSelectedItem:
                    return new object[] { context.RowItem, context.SelectedItem };
                case CommitParameterMode.LookupCommitContext:
                default:
                    return context;
            }
        }

        private static void ApplyBinding(DependencyObject target, DependencyProperty property, BindingBase binding)
        {
            if (target == null || property == null || binding == null)
            {
                return;
            }

            BindingOperations.SetBinding(target, property, CloneBinding(binding));
        }

        private static BindingBase CloneBinding(BindingBase source)
        {
            var binding = source as Binding;
            if (binding == null)
            {
                return source;
            }

            var clone = new Binding
            {
                Path = binding.Path,
                XPath = binding.XPath,
                Mode = binding.Mode,
                UpdateSourceTrigger = binding.UpdateSourceTrigger,
                Converter = binding.Converter,
                ConverterCulture = binding.ConverterCulture,
                ConverterParameter = binding.ConverterParameter,
                FallbackValue = binding.FallbackValue,
                StringFormat = binding.StringFormat,
                TargetNullValue = binding.TargetNullValue,
                ValidatesOnDataErrors = binding.ValidatesOnDataErrors,
                ValidatesOnExceptions = binding.ValidatesOnExceptions,
                NotifyOnValidationError = binding.NotifyOnValidationError
            };

            if (binding.Source != null)
            {
                clone.Source = binding.Source;
            }
            else if (binding.RelativeSource != null)
            {
                clone.RelativeSource = binding.RelativeSource;
            }
            else if (!string.IsNullOrWhiteSpace(binding.ElementName))
            {
                clone.ElementName = binding.ElementName;
            }

            return clone;
        }
    }
}
