using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MateWpfThemes.FW.Controls
{
    public enum SearchableTextBoxSearchMode
    {
        Auto,
        Local,
        Remote
    }

    /// <summary>
    /// 可搜索、可选择、可自定义下拉模板的搜索框。
    /// 中文注释：Plus 版本把搜索输入和选中项显示分离，避免选中后回填输入框再次触发搜索。
    /// </summary>
    public partial class SearchableTextBoxPlus : UserControl
    {
        private readonly DispatcherTimer _debounceTimer;
        private readonly Dictionary<Type, Dictionary<string, PropertyInfo>> _propertyCache =
            new Dictionary<Type, Dictionary<string, PropertyInfo>>();

        private bool _isInternalChanging;
        private bool _isSelectionDisplayMode;
        private INotifyCollectionChanged _itemsCollectionChangedSource;

        public SearchableTextBoxPlus()
        {
            InitializeComponent();

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _debounceTimer.Tick += DebounceTimer_Tick;

            Loaded += SearchableTextBoxPlus_Loaded;
            Unloaded += SearchableTextBoxPlus_Unloaded;
        }

        #region Placeholder

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder),
                typeof(string),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(string.Empty, OnPlaceholderChanged));

        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SearchableTextBoxPlus)d).UpdateTextVisualState();
        }

        #endregion

        #region SearchMode

        public static readonly DependencyProperty SearchModeProperty =
            DependencyProperty.Register(
                nameof(SearchMode),
                typeof(SearchableTextBoxSearchMode),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(SearchableTextBoxSearchMode.Auto));

        public SearchableTextBoxSearchMode SearchMode
        {
            get { return (SearchableTextBoxSearchMode)GetValue(SearchModeProperty); }
            set { SetValue(SearchModeProperty, value); }
        }

        #endregion

        #region Text

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(SearchableTextBoxPlus),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnTextChangedCallback));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        private static void OnTextChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableTextBoxPlus)d;
            if (control._isInternalChanging)
            {
                return;
            }

            control._isSelectionDisplayMode = false;
            control.SetInputTextSilently(e.NewValue == null ? string.Empty : e.NewValue.ToString());
            control.UpdateTextVisualState();
        }

        #endregion

        #region ItemsSource

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableTextBoxPlus)d;
            control.DetachCollectionChanged();

            if (e.NewValue is IEnumerable source)
            {
                var cvs = new CollectionViewSource { Source = source };
                control.FilteredView = cvs.View;
                control.FilteredView.Filter = control.FilterItem;

                if (control.ResultList != null)
                {
                    control.ResultList.ItemsSource = control.FilteredView;
                    control.ApplyItemTemplate();
                }

                control.AttachCollectionChanged(source);
                control.FilteredView.Refresh();
                control.OpenDropDownIfHasItems();
            }
            else
            {
                control.FilteredView = null;

                if (control.ResultList != null)
                {
                    control.ResultList.ItemsSource = null;
                }

                control.SetSelectedItemSilently(null);
                control.SelectedValue = null;
                control.SetDisplayText(string.Empty);
                control.IsDropDownOpen = false;
            }

            control.UpdateTextVisualState();
        }

        #endregion

        #region DisplayMemberPath

        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register(
                nameof(DisplayMemberPath),
                typeof(string),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(string.Empty, OnDisplayMemberPathChanged));

        public string DisplayMemberPath
        {
            get { return (string)GetValue(DisplayMemberPathProperty); }
            set { SetValue(DisplayMemberPathProperty, value); }
        }

        private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableTextBoxPlus)d;
            control.ApplyItemTemplate();
            control.UpdateDisplayTextFromSelectedItem();
        }

        #endregion

        #region FilterMemberPaths

        public static readonly DependencyProperty FilterMemberPathsProperty =
            DependencyProperty.Register(
                nameof(FilterMemberPaths),
                typeof(string),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(string.Empty));

        public string FilterMemberPaths
        {
            get { return (string)GetValue(FilterMemberPathsProperty); }
            set { SetValue(FilterMemberPathsProperty, value); }
        }

        #endregion

        #region RequireItemMatch

        public static readonly DependencyProperty RequireItemMatchProperty =
            DependencyProperty.Register(
                nameof(RequireItemMatch),
                typeof(bool),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(false));

        public bool RequireItemMatch
        {
            get { return (bool)GetValue(RequireItemMatchProperty); }
            set { SetValue(RequireItemMatchProperty, value); }
        }

        #endregion

        #region SelectedItem

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(object),
                typeof(SearchableTextBoxPlus),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemChanged));

        public object SelectedItem
        {
            get { return GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableTextBoxPlus)d;
            if (control._isInternalChanging)
            {
                return;
            }

            if (e.NewValue == null)
            {
                control.SelectedValue = null;
                control.SetDisplayText(string.Empty);
                control._isSelectionDisplayMode = false;
                control.UpdateTextVisualState();
                return;
            }

            control.SelectedValue = control.GetSelectedValue(e.NewValue);
            control.SetDisplayText(control.GetDisplayText(e.NewValue));
            control.ClearInputTextSilently();
            control._isSelectionDisplayMode = true;
            control.UpdateTextVisualState();
        }

        #endregion

        #region SelectionCommittedCommand

        public static readonly DependencyProperty SelectionCommittedCommandProperty =
            DependencyProperty.Register(
                nameof(SelectionCommittedCommand),
                typeof(ICommand),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(null));

        public ICommand SelectionCommittedCommand
        {
            get { return (ICommand)GetValue(SelectionCommittedCommandProperty); }
            set { SetValue(SelectionCommittedCommandProperty, value); }
        }

        public static readonly DependencyProperty SelectionCommittedCommandParameterProperty =
            DependencyProperty.Register(
                nameof(SelectionCommittedCommandParameter),
                typeof(object),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(null));

        public object SelectionCommittedCommandParameter
        {
            get { return GetValue(SelectionCommittedCommandParameterProperty); }
            set { SetValue(SelectionCommittedCommandParameterProperty, value); }
        }

        private static readonly DependencyPropertyKey CommitReasonPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(CommitReason),
                typeof(LookupCommitReason),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(LookupCommitReason.Programmatic));

        public static readonly DependencyProperty CommitReasonProperty = CommitReasonPropertyKey.DependencyProperty;

        public LookupCommitReason CommitReason
        {
            get { return (LookupCommitReason)GetValue(CommitReasonProperty); }
            private set { SetValue(CommitReasonPropertyKey, value); }
        }

        public event EventHandler<LookupSelectionCommittedEventArgs> SelectionCommitted;

        #endregion

        #region SelectedValuePath / SelectedValue

        public static readonly DependencyProperty SelectedValuePathProperty =
            DependencyProperty.Register(
                nameof(SelectedValuePath),
                typeof(string),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(string.Empty));

        public string SelectedValuePath
        {
            get { return (string)GetValue(SelectedValuePathProperty); }
            set { SetValue(SelectedValuePathProperty, value); }
        }

        public static readonly DependencyProperty SelectedValueProperty =
            DependencyProperty.Register(
                nameof(SelectedValue),
                typeof(object),
                typeof(SearchableTextBoxPlus),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object SelectedValue
        {
            get { return GetValue(SelectedValueProperty); }
            set { SetValue(SelectedValueProperty, value); }
        }

        #endregion

        #region IsDropDownOpen / ItemTemplate / HasTextOrSelection

        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register(
                nameof(IsDropDownOpen),
                typeof(bool),
                typeof(SearchableTextBoxPlus),
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool IsDropDownOpen
        {
            get { return (bool)GetValue(IsDropDownOpenProperty); }
            set { SetValue(IsDropDownOpenProperty, value); }
        }

        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(
                nameof(ItemTemplate),
                typeof(DataTemplate),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(null, OnItemTemplateChanged));

        public DataTemplate ItemTemplate
        {
            get { return (DataTemplate)GetValue(ItemTemplateProperty); }
            set { SetValue(ItemTemplateProperty, value); }
        }

        private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SearchableTextBoxPlus)d).ApplyItemTemplate();
        }

        public static readonly DependencyProperty HasTextOrSelectionProperty =
            DependencyProperty.Register(
                nameof(HasTextOrSelection),
                typeof(bool),
                typeof(SearchableTextBoxPlus),
                new PropertyMetadata(false));

        public bool HasTextOrSelection
        {
            get { return (bool)GetValue(HasTextOrSelectionProperty); }
            private set { SetValue(HasTextOrSelectionProperty, value); }
        }

        public ICollectionView FilteredView { get; private set; }

        #endregion

        public void FocusEditor()
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    InputTextBox.Focus();
                    Keyboard.Focus(InputTextBox);
                    InputTextBox.SelectAll();
                }),
                DispatcherPriority.Input);
        }

        #region Events

        private void SearchableTextBoxPlus_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyItemTemplate();
            UpdateDisplayTextFromSelectedItem();
            UpdateTextVisualState();
            if (IsKeyboardFocusWithin || IsFocused)
            {
                FocusEditor();
            }
        }

        private void SearchableTextBoxPlus_Unloaded(object sender, RoutedEventArgs e)
        {
            _debounceTimer.Stop();
            DetachCollectionChanged();
        }

        private void InputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateTextVisualState();
            OpenDropDownIfHasItems();
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInternalChanging)
            {
                return;
            }

            var newText = InputTextBox.Text ?? string.Empty;

            _isSelectionDisplayMode = false;

            if (SelectedItem != null)
            {
                SetSelectedItemSilently(null);
                SelectedValue = null;
                SetDisplayText(string.Empty);
            }

            SyncTextDependencySilently(newText);
            UpdateTextVisualState();

            if (ShouldUseLocalFilter())
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            else
            {
                _debounceTimer.Stop();
                OpenDropDownIfHasItems();
            }
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();

            if (!ShouldUseLocalFilter())
            {
                return;
            }

            ResolveSelectionByText();
            RefreshAndUpdateDropDown();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _debounceTimer.Stop();

            _isSelectionDisplayMode = false;

            SetSelectedItemSilently(null);
            SelectedValue = null;
            SetDisplayText(string.Empty);

            SetInputTextSilently(string.Empty);
            SyncTextDependencySilently(string.Empty);

            FilteredView?.Refresh();
            IsDropDownOpen = FilteredView != null && !FilteredView.IsEmpty;

            UpdateTextVisualState();
            InputTextBox.Focus();
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                FilteredView?.Refresh();

                if (FilteredView != null && !FilteredView.IsEmpty)
                {
                    ResultList.SelectedIndex = 0;
                    IsDropDownOpen = true;
                }

                e.Handled = true;
                return;
            }

            if (!IsDropDownOpen || ResultList.Items.Count == 0)
            {
                return;
            }

            if (e.Key == Key.Down)
            {
                ResultList.SelectedIndex = Math.Min(ResultList.SelectedIndex + 1, ResultList.Items.Count - 1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex - 1, 0);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                CommitSelection(ResultList.SelectedItem, e.Key == Key.Tab ? LookupCommitReason.Tab : LookupCommitReason.Enter);
                e.Handled = e.Key != Key.Tab;
            }
            else if (e.Key == Key.Escape)
            {
                IsDropDownOpen = false;
                e.Handled = true;
            }
        }

        private void InputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!RequireItemMatch || IsDropDownOpen)
            {
                UpdateTextVisualState();
                return;
            }

            var currentText = (InputTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(currentText))
            {
                UpdateTextVisualState();
                return;
            }

            var hasMatch = ResolveSelectionByText();
            if (!hasMatch)
            {
                SetInputTextSilently(string.Empty);
                SyncTextDependencySilently(string.Empty);
                SetSelectedItemSilently(null);
                SelectedValue = null;
                SetDisplayText(string.Empty);
            }

            UpdateTextVisualState();
        }

        private void ResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(e.OriginalSource is DependencyObject originalSource))
            {
                return;
            }

            var clickedItem = FindAncestor<ListBoxItem>(originalSource);
            if (clickedItem == null)
            {
                return;
            }

            CommitSelection(clickedItem.DataContext, LookupCommitReason.Mouse);
            e.Handled = true;
        }

        private void EditorBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSelectionDisplayMode)
            {
                BeginEditSelection();
                e.Handled = true;
                return;
            }

            InputTextBox.Focus();
        }

        private void ItemsSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ItemsSource_CollectionChanged(sender, e)));
                return;
            }

            FilteredView?.Refresh();

            if (!ShouldUseLocalFilter() && InputTextBox.IsKeyboardFocusWithin)
            {
                var hasInput = !string.IsNullOrWhiteSpace(InputTextBox.Text);
                IsDropDownOpen = hasInput && FilteredView != null && !FilteredView.IsEmpty;
            }
        }

        #endregion

        #region Search and selection

        private bool FilterItem(object item)
        {
            if (!ShouldUseLocalFilter())
            {
                return true;
            }

            var input = InputTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            foreach (var path in ParseFilterMemberPaths())
            {
                var value = GetMemberText(item, path);
                if (!string.IsNullOrEmpty(value) &&
                    value.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void CommitSelection(object item, LookupCommitReason reason)
        {
            if (item == null)
            {
                return;
            }

            _debounceTimer.Stop();

            SetSelectedItemSilently(item);
            SelectedValue = GetSelectedValue(item);
            SetDisplayText(GetDisplayText(item));

            // 中文注释：选中后显示交给 DisplayTextBlock，InputTextBox 继续只表示搜索输入。
            ClearInputTextSilently();
            _isSelectionDisplayMode = true;

            IsDropDownOpen = false;
            CommitReason = reason;
            UpdateTextVisualState();
            ExecuteSelectionCommittedCommand(item, reason);
            SelectionCommitted?.Invoke(
                this,
                new LookupSelectionCommittedEventArgs(item, SelectedValue, Text, reason));
        }

        private bool ResolveSelectionByText()
        {
            var text = (InputTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || ItemsSource == null)
            {
                return string.IsNullOrWhiteSpace(text);
            }

            foreach (var item in ItemsSource)
            {
                if (item == null)
                {
                    continue;
                }

                foreach (var path in ParseResolveMemberPaths())
                {
                    var value = GetMemberText(item, path);
                    if (string.Equals((value ?? string.Empty).Trim(), text, StringComparison.OrdinalIgnoreCase))
                    {
                        SetSelectedItemSilently(item);
                        SelectedValue = GetSelectedValue(item);
                        SetDisplayText(GetDisplayText(item));
                        CommitReason = LookupCommitReason.ExactMatch;
                        return true;
                    }
                }
            }

            SetSelectedItemSilently(null);
            SelectedValue = null;
            SetDisplayText(string.Empty);
            return false;
        }

        private void BeginEditSelection()
        {
            _isSelectionDisplayMode = false;

            SetSelectedItemSilently(null);
            SelectedValue = null;
            SetDisplayText(string.Empty);
            SetInputTextSilently(string.Empty);
            SyncTextDependencySilently(string.Empty);

            IsDropDownOpen = false;
            UpdateTextVisualState();
            InputTextBox.Focus();
        }

        private void ExecuteSelectionCommittedCommand(object item, LookupCommitReason reason)
        {
            var command = SelectionCommittedCommand;
            var parameter = SelectionCommittedCommandParameter ??
                            new LookupSelectionCommittedEventArgs(item, SelectedValue, Text, reason);

            if (SelectionCommittedCommandParameter == null)
            {
                parameter = item;
            }

            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }

        #endregion

        #region View helpers

        private void ApplyItemTemplate()
        {
            if (ResultList == null)
            {
                return;
            }

            if (ItemTemplate != null)
            {
                ResultList.DisplayMemberPath = string.Empty;
                ResultList.ItemTemplate = ItemTemplate;
            }
            else
            {
                ResultList.ItemTemplate = null;
                ResultList.DisplayMemberPath = DisplayMemberPath ?? string.Empty;
            }
        }

        private void RefreshAndUpdateDropDown()
        {
            if (FilteredView == null)
            {
                IsDropDownOpen = false;
                return;
            }

            FilteredView.Refresh();
            IsDropDownOpen = !FilteredView.IsEmpty;
        }

        private void OpenDropDownIfHasItems()
        {
            if (FilteredView == null)
            {
                return;
            }

            FilteredView.Refresh();

            if (!ShouldUseLocalFilter() && string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                IsDropDownOpen = false;
                return;
            }

            IsDropDownOpen = !FilteredView.IsEmpty;
        }

        private void UpdateDisplayTextFromSelectedItem()
        {
            SetDisplayText(SelectedItem == null ? string.Empty : GetDisplayText(SelectedItem));
        }

        private void SetDisplayText(string text)
        {
            if (DisplayTextBlock == null)
            {
                return;
            }

            DisplayTextBlock.Text = text ?? string.Empty;
            UpdateTextVisualState();
        }

        private void UpdateTextVisualState()
        {
            if (InputTextBox == null || DisplayTextBlock == null || PlaceholderTextBlock == null)
            {
                return;
            }

            var hasInput = !string.IsNullOrWhiteSpace(InputTextBox.Text);
            var hasDisplay = !string.IsNullOrWhiteSpace(DisplayTextBlock.Text);

            if (_isSelectionDisplayMode && hasDisplay)
            {
                InputTextBox.Visibility = Visibility.Collapsed;
                DisplayTextBlock.Visibility = Visibility.Visible;
                PlaceholderTextBlock.Visibility = Visibility.Collapsed;
                HasTextOrSelection = true;
                return;
            }

            InputTextBox.Visibility = Visibility.Visible;
            DisplayTextBlock.Visibility = Visibility.Collapsed;
            PlaceholderTextBlock.Visibility = hasInput || hasDisplay ? Visibility.Collapsed : Visibility.Visible;
            HasTextOrSelection = hasInput || hasDisplay;
        }

        #endregion

        #region Member helpers

        private bool ShouldUseLocalFilter()
        {
            if (SearchMode == SearchableTextBoxSearchMode.Local)
            {
                return true;
            }

            if (SearchMode == SearchableTextBoxSearchMode.Remote)
            {
                return false;
            }

            var bindingExpression = BindingOperations.GetBindingExpression(this, TextProperty);
            return bindingExpression == null;
        }

        private string GetDisplayText(object item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(DisplayMemberPath))
            {
                var prop = GetCachedProperty(item.GetType(), DisplayMemberPath);
                return prop?.GetValue(item)?.ToString() ?? string.Empty;
            }

            return item.ToString() ?? string.Empty;
        }

        private object GetSelectedValue(object item)
        {
            if (item == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(SelectedValuePath))
            {
                return item;
            }

            var prop = GetCachedProperty(item.GetType(), SelectedValuePath);
            return prop?.GetValue(item);
        }

        private string GetMemberText(object item, string memberPath)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(memberPath))
            {
                return item.ToString() ?? string.Empty;
            }

            var prop = GetCachedProperty(item.GetType(), memberPath);
            return prop?.GetValue(item)?.ToString() ?? string.Empty;
        }

        private PropertyInfo GetCachedProperty(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            if (!_propertyCache.TryGetValue(type, out var typeProps))
            {
                typeProps = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
                _propertyCache[type] = typeProps;
            }

            if (!typeProps.TryGetValue(propertyName, out var propInfo))
            {
                propInfo = type.GetProperty(
                    propertyName,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                typeProps[propertyName] = propInfo;
            }

            return propInfo;
        }

        private List<string> ParseFilterMemberPaths()
        {
            if (!string.IsNullOrWhiteSpace(FilterMemberPaths))
            {
                var paths = FilterMemberPaths
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (paths.Count > 0)
                {
                    return paths;
                }
            }

            if (!string.IsNullOrWhiteSpace(DisplayMemberPath))
            {
                return new List<string> { DisplayMemberPath };
            }

            return new List<string> { string.Empty };
        }

        private List<string> ParseResolveMemberPaths()
        {
            var paths = ParseFilterMemberPaths();

            if (!string.IsNullOrWhiteSpace(DisplayMemberPath) &&
                !paths.Contains(DisplayMemberPath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Insert(0, DisplayMemberPath);
            }

            return paths;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        #endregion

        #region Silent sync

        private void SetSelectedItemSilently(object item)
        {
            if (Equals(SelectedItem, item))
            {
                return;
            }

            _isInternalChanging = true;
            try
            {
                SelectedItem = item;
            }
            finally
            {
                _isInternalChanging = false;
            }
        }

        private void SetInputTextSilently(string text)
        {
            var target = text ?? string.Empty;
            var current = InputTextBox.Text ?? string.Empty;

            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return;
            }

            _isInternalChanging = true;
            try
            {
                InputTextBox.Text = target;
                InputTextBox.CaretIndex = target.Length;
            }
            finally
            {
                _isInternalChanging = false;
            }
        }

        private void ClearInputTextSilently()
        {
            SetInputTextSilently(string.Empty);
        }

        private void SyncTextDependencySilently(string text)
        {
            var target = text ?? string.Empty;
            var current = Text ?? string.Empty;

            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return;
            }

            _isInternalChanging = true;
            try
            {
                Text = target;
            }
            finally
            {
                _isInternalChanging = false;
            }
        }

        private void AttachCollectionChanged(IEnumerable source)
        {
            _itemsCollectionChangedSource = source as INotifyCollectionChanged;
            if (_itemsCollectionChangedSource != null)
            {
                _itemsCollectionChangedSource.CollectionChanged += ItemsSource_CollectionChanged;
            }
        }

        private void DetachCollectionChanged()
        {
            if (_itemsCollectionChangedSource != null)
            {
                _itemsCollectionChangedSource.CollectionChanged -= ItemsSource_CollectionChanged;
                _itemsCollectionChangedSource = null;
            }
        }

        #endregion
    }
}
