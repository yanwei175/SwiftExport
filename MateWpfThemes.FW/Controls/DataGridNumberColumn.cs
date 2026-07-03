using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MateWpfThemes.FW.Controls
{
    /// <summary>
    /// DataGrid 通用数字编辑列，负责输入限制和显示格式，不承载业务校验。
    /// </summary>
    public class DataGridNumberColumn : DataGridBoundColumn
    {
        private static readonly Regex NumericTextRegex = new Regex(@"^[0-9\.\-]+$", RegexOptions.Compiled);

        public int DecimalPlaces { get; set; } = 2;
        public string FormatString { get; set; } = "N2";
        public bool AllowNegative { get; set; }
        public bool SelectAllOnFocus { get; set; } = true;
        public bool CommitOnEnter { get; set; } = true;

        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
        {
            var textBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(3)
            };

            var binding = Binding as Binding;
            if (binding != null)
            {
                var displayBinding = CloneBinding(binding);
                if (string.IsNullOrWhiteSpace(displayBinding.StringFormat) && !string.IsNullOrWhiteSpace(FormatString))
                {
                    displayBinding.StringFormat = "{0:" + FormatString + "}";
                }

                BindingOperations.SetBinding(textBlock, TextBlock.TextProperty, displayBinding);
            }
            else if (Binding != null)
            {
                BindingOperations.SetBinding(textBlock, TextBlock.TextProperty, Binding);
            }

            return textBlock;
        }

        protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
        {
            var textBox = new TextBox
            {
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 0, 2, 0)
            };

            if (Binding is Binding binding)
            {
                var editBinding = CloneBinding(binding);
                editBinding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
                BindingOperations.SetBinding(textBox, TextBox.TextProperty, editBinding);
            }
            else if (Binding != null)
            {
                BindingOperations.SetBinding(textBox, TextBox.TextProperty, Binding);
            }

            textBox.PreviewTextInput += OnPreviewTextInput;
            textBox.PreviewKeyDown += OnPreviewKeyDown;
            textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            textBox.LostKeyboardFocus += OnLostKeyboardFocus;
            DataObject.AddPastingHandler(textBox, OnPaste);

            return textBox;
        }

        protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
        {
            if (editingElement is TextBox textBox)
            {
                textBox.Focus();
                if (SelectAllOnFocus)
                {
                    textBox.SelectAll();
                }

                return textBox.Text;
            }

            return null;
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                return;
            }

            var nextText = GetNextText(textBox, e.Text);
            e.Handled = !IsValidInput(nextText, allowIntermediate: true);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                return;
            }

            if (CommitOnEnter && e.Key == Key.Enter)
            {
                var dataGrid = FindAncestor<DataGrid>((DependencyObject)sender);
                dataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            }
        }

        private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (SelectAllOnFocus && sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                return;
            }

            if (decimal.TryParse(textBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
            {
                textBox.Text = Math.Round(value, DecimalPlaces).ToString(FormatString, CultureInfo.CurrentCulture);
            }
        }

        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!(sender is TextBox textBox) ||
                !e.DataObject.GetDataPresent(DataFormats.Text))
            {
                return;
            }

            var pastedText = e.DataObject.GetData(DataFormats.Text) as string;
            var nextText = GetNextText(textBox, pastedText ?? string.Empty);
            if (!IsValidInput(nextText, allowIntermediate: false))
            {
                e.CancelCommand();
            }
        }

        private bool IsValidInput(string text, bool allowIntermediate)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!NumericTextRegex.IsMatch(text))
            {
                return false;
            }

            if (!AllowNegative && text.Contains("-"))
            {
                return false;
            }

            if (text.IndexOf('-') > 0 || text.Split('-').Length > 2)
            {
                return false;
            }

            if (text.Split('.').Length > 2)
            {
                return false;
            }

            var decimalIndex = text.IndexOf('.');
            if (decimalIndex >= 0 && text.Length - decimalIndex - 1 > DecimalPlaces)
            {
                return false;
            }

            if (allowIntermediate && (text == "-" || text == "." || text == "-."))
            {
                return AllowNegative || text != "-";
            }

            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out _);
        }

        private static string GetNextText(TextBox textBox, string input)
        {
            var text = textBox.Text ?? string.Empty;
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;

            if (selectionLength > 0)
            {
                text = text.Remove(selectionStart, selectionLength);
            }

            return text.Insert(selectionStart, input ?? string.Empty);
        }

        private static Binding CloneBinding(Binding source)
        {
            var clone = new Binding
            {
                Path = source.Path,
                XPath = source.XPath,
                Mode = source.Mode,
                UpdateSourceTrigger = source.UpdateSourceTrigger,
                Converter = source.Converter,
                ConverterCulture = source.ConverterCulture,
                ConverterParameter = source.ConverterParameter,
                FallbackValue = source.FallbackValue,
                StringFormat = source.StringFormat,
                TargetNullValue = source.TargetNullValue,
                ValidatesOnDataErrors = source.ValidatesOnDataErrors,
                ValidatesOnExceptions = source.ValidatesOnExceptions,
                NotifyOnValidationError = source.NotifyOnValidationError
            };

            if (source.Source != null)
            {
                clone.Source = source.Source;
            }
            else if (source.RelativeSource != null)
            {
                clone.RelativeSource = source.RelativeSource;
            }
            else if (!string.IsNullOrWhiteSpace(source.ElementName))
            {
                clone.ElementName = source.ElementName;
            }

            return clone;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
