using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.VSTO.Resources.Controls
{

    /// <summary>决定候选由控件本地过滤还是由页面命令提供。</summary>
    public enum LookupTextBoxSearchMode
    {
        /// <summary>Text 无绑定时本地过滤，否则由调用方更新候选。</summary>
        Auto,
        /// <summary>始终在当前候选上本地过滤。</summary>
        Local,
        /// <summary>以防抖命令通知页面查询，控件不调用 API。</summary>
        Remote,
    }

    /// <summary>提供输入、候选选择及可选的宿主高度填充行为。</summary>
    public partial class LookupTextBox : UserControl
    {
        /// <summary>标识编辑区域是否填满宿主分配的高度，默认按内容高度显示。</summary>
        public static readonly DependencyProperty StretchToHostProperty = DependencyProperty.Register(
            nameof(StretchToHost), typeof(bool), typeof(LookupTextBox), new PropertyMetadata(false));

        /// <summary>获取或设置是否纵向填满宿主；DataGrid 单元格中可开启，内部最小尺寸仍有效。</summary>
        public bool StretchToHost
        {
            get => (bool)GetValue(StretchToHostProperty);
            set => SetValue(StretchToHostProperty, value);
        }

        private readonly DispatcherTimer _debounceTimer;
        private readonly Dictionary<Type, Dictionary<string, PropertyInfo>> _propertyCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        private int _lifetimeVersion;
        private bool _isInternalChanging;
        private bool _isSelectionDisplayMode;
        private bool _hasUserInputIntent;
        private bool _dropDownWasOpenOnButtonPress;
        private INotifyCollectionChanged _itemsCollectionChangedSource;
        private ILookupResultHost _customResultHost;

        /// <summary>创建编辑器及本控件拥有的防抖计时器，不加载缓存或请求网络。</summary>
        public LookupTextBox()
        {
            InitializeComponent();

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounceTimer.Tick += DebounceTimer_Tick;

            Loaded += LookupTextBox_Loaded;
            Unloaded += LookupTextBox_Unloaded;
            IsVisibleChanged += LookupTextBox_IsVisibleChanged;
        }

        /// <summary>未选择时显示的提示文字的依赖属性标识。</summary>
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder),
                typeof(string),
                typeof(LookupTextBox),
                new PropertyMetadata(string.Empty, OnPlaceholderChanged));

        /// <summary>未选择时显示的提示文字。</summary>
        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        /// <summary>本地过滤、远程命令或按 Text 绑定自动判断的搜索模式的依赖属性标识。</summary>
        public static readonly DependencyProperty SearchModeProperty =
            DependencyProperty.Register(
                nameof(SearchMode),
                typeof(LookupTextBoxSearchMode),
                typeof(LookupTextBox),
                new PropertyMetadata(LookupTextBoxSearchMode.Auto));

        /// <summary>本地过滤、远程命令或按 Text 绑定自动判断的搜索模式。</summary>
        public LookupTextBoxSearchMode SearchMode
        {
            get => (LookupTextBoxSearchMode)GetValue(SearchModeProperty);
            set => SetValue(SearchModeProperty, value);
        }

        /// <summary>防抖后接收搜索文本的命令，控件本身不执行网络请求的依赖属性标识。</summary>
        public static readonly DependencyProperty SearchCommandProperty =
            DependencyProperty.Register(
                nameof(SearchCommand),
                typeof(ICommand),
                typeof(LookupTextBox),
                new PropertyMetadata(null));

        /// <summary>防抖后接收搜索文本的命令，控件本身不执行网络请求。</summary>
        public ICommand SearchCommand
        {
            get => (ICommand)GetValue(SearchCommandProperty);
            set => SetValue(SearchCommandProperty, value);
        }

        /// <summary>无候选提交时按 Enter 执行的查询命令的依赖属性标识。</summary>
        public static readonly DependencyProperty QueryCommandProperty =
            DependencyProperty.Register(
                nameof(QueryCommand),
                typeof(ICommand),
                typeof(LookupTextBox),
                new PropertyMetadata(null));

        /// <summary>无候选提交时按 Enter 执行的查询命令。</summary>
        public ICommand QueryCommand
        {
            get => (ICommand)GetValue(QueryCommandProperty);
            set => SetValue(QueryCommandProperty, value);
        }

        /// <summary>远程搜索防抖间隔，必须非负的依赖属性标识。</summary>
        public static readonly DependencyProperty SearchDelayProperty =
            DependencyProperty.Register(
                nameof(SearchDelay),
                typeof(TimeSpan),
                typeof(LookupTextBox),
                new PropertyMetadata(TimeSpan.FromMilliseconds(300), OnSearchDelayChanged),
                value => value is TimeSpan delay && delay >= TimeSpan.Zero);

        /// <summary>远程搜索防抖间隔，必须非负。</summary>
        public TimeSpan SearchDelay
        {
            get => (TimeSpan)GetValue(SearchDelayProperty);
            set => SetValue(SearchDelayProperty, value);
        }

        /// <summary>允许搜索的最少字符数的依赖属性标识。</summary>
        public static readonly DependencyProperty MinimumSearchLengthProperty =
            DependencyProperty.Register(
                nameof(MinimumSearchLength),
                typeof(int),
                typeof(LookupTextBox),
                new PropertyMetadata(1, null, (_, value) => Math.Max(0, (int)value)));

        /// <summary>允许搜索的最少字符数。</summary>
        public int MinimumSearchLength
        {
            get => (int)GetValue(MinimumSearchLengthProperty);
            set => SetValue(MinimumSearchLengthProperty, value);
        }

        /// <summary>外部查询的忙碌显示状态的依赖属性标识。</summary>
        public static readonly DependencyProperty IsBusyProperty =
            DependencyProperty.Register(
                nameof(IsBusy),
                typeof(bool),
                typeof(LookupTextBox),
                new PropertyMetadata(false));

        /// <summary>外部查询的忙碌显示状态。</summary>
        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }

        /// <summary>搜索输入文本，不等同于已选项的显示文字的依赖属性标识。</summary>
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(LookupTextBox),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnTextChangedCallback));

        /// <summary>搜索输入文本，不等同于已选项的显示文字。</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>候选数据源；集合变更必须由页面在 UI 线程发布的依赖属性标识。</summary>
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(LookupTextBox),
                new PropertyMetadata(null, OnItemsSourceChanged));

        /// <summary>候选数据源；集合变更必须由页面在 UI 线程发布。</summary>
        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>候选显示文字的属性名的依赖属性标识。</summary>
        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register(
                nameof(DisplayMemberPath),
                typeof(string),
                typeof(LookupTextBox),
                new PropertyMetadata(string.Empty, OnDisplayMemberPathChanged));

        /// <summary>候选显示文字的属性名。</summary>
        public string DisplayMemberPath
        {
            get => (string)GetValue(DisplayMemberPathProperty);
            set => SetValue(DisplayMemberPathProperty, value);
        }

        /// <summary>参与本地匹配的属性名，以逗号、分号或竖线分隔的依赖属性标识。</summary>
        public static readonly DependencyProperty FilterMemberPathsProperty =
            DependencyProperty.Register(
                nameof(FilterMemberPaths),
                typeof(string),
                typeof(LookupTextBox),
                new PropertyMetadata(string.Empty));

        /// <summary>参与本地匹配的属性名，以逗号、分号或竖线分隔。</summary>
        public string FilterMemberPaths
        {
            get => (string)GetValue(FilterMemberPathsProperty);
            set => SetValue(FilterMemberPathsProperty, value);
        }

        /// <summary>失焦时是否要求输入匹配现有候选的依赖属性标识。</summary>
        public static readonly DependencyProperty RequireItemMatchProperty =
            DependencyProperty.Register(
                nameof(RequireItemMatch),
                typeof(bool),
                typeof(LookupTextBox),
                new PropertyMetadata(false));

        /// <summary>失焦时是否要求输入匹配现有候选。</summary>
        public bool RequireItemMatch
        {
            get => (bool)GetValue(RequireItemMatchProperty);
            set => SetValue(RequireItemMatchProperty, value);
        }

        /// <summary>进入编辑时是否立即清除原选择的依赖属性标识。</summary>
        public static readonly DependencyProperty ClearSelectionOnEditProperty =
            DependencyProperty.Register(
                nameof(ClearSelectionOnEdit),
                typeof(bool),
                typeof(LookupTextBox),
                new PropertyMetadata(true));

        /// <summary>
        /// 获取或设置已有选择进入搜索编辑态时是否立即清空选择。默认清空以兼容既有行为；
        /// 筛选场景可设为 false，使旧选择保留到提交新候选或显式清除为止。
        /// </summary>
        public bool ClearSelectionOnEdit
        {
            get => (bool)GetValue(ClearSelectionOnEditProperty);
            set => SetValue(ClearSelectionOnEditProperty, value);
        }

        /// <summary>标识是否显示用于显式展开或收起候选的按钮；默认关闭以保持旧调用方外观。</summary>
        public static readonly DependencyProperty ShowDropDownButtonProperty = DependencyProperty.Register(
            nameof(ShowDropDownButton), typeof(bool), typeof(LookupTextBox), new PropertyMetadata(false));

        /// <summary>获取或设置是否显示候选展开按钮。</summary>
        public bool ShowDropDownButton
        {
            get => (bool)GetValue(ShowDropDownButtonProperty);
            set => SetValue(ShowDropDownButtonProperty, value);
        }

        /// <summary>标识候选列表是否跟随编辑器 FontSize；默认关闭以保持旧调用方字号。</summary>
        public static readonly DependencyProperty MatchSuggestionFontSizeProperty = DependencyProperty.Register(
            nameof(MatchSuggestionFontSize), typeof(bool), typeof(LookupTextBox),
            new PropertyMetadata(false, OnMatchSuggestionFontSizeChanged));

        /// <summary>获取或设置候选文字是否使用编辑器 FontSize。</summary>
        public bool MatchSuggestionFontSize
        {
            get => (bool)GetValue(MatchSuggestionFontSizeProperty);
            set => SetValue(MatchSuggestionFontSizeProperty, value);
        }

        /// <summary>切换候选字号的绑定来源，关闭时恢复默认资源。</summary>
        private static void OnMatchSuggestionFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LookupTextBox)d;
            if ((bool)e.NewValue)
            {
                BindingOperations.SetBinding(
                    control.ResultList,
                    Control.FontSizeProperty,
                    new Binding(nameof(FontSize)) { Source = control });
                return;
            }

            control.ResultList.SetResourceReference(Control.FontSizeProperty, "SwiftExportDefaltFontSize");
        }

        /// <summary>当前选择的候选对象，默认双向绑定的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(object),
                typeof(LookupTextBox),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemChanged));

        /// <summary>当前选择的候选对象，默认双向绑定。</summary>
        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        /// <summary>用户提交候选后执行的命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandProperty =
            DependencyProperty.Register(
                nameof(SelectionCommittedCommand),
                typeof(ICommand),
                typeof(LookupTextBox),
                new PropertyMetadata(null));

        /// <summary>用户提交候选后执行的命令。</summary>
        public ICommand SelectionCommittedCommand
        {
            get => (ICommand)GetValue(SelectionCommittedCommandProperty);
            set => SetValue(SelectionCommittedCommandProperty, value);
        }

        /// <summary>提交命令的可选参数，未指定时传入候选的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandParameterProperty =
            DependencyProperty.Register(
                nameof(SelectionCommittedCommandParameter),
                typeof(object),
                typeof(LookupTextBox),
                new PropertyMetadata(null));

        /// <summary>提交命令的可选参数，未指定时传入候选。</summary>
        public object SelectionCommittedCommandParameter
        {
            get => GetValue(SelectionCommittedCommandParameterProperty);
            set => SetValue(SelectionCommittedCommandParameterProperty, value);
        }

        private static readonly DependencyPropertyKey CommitReasonPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(CommitReason),
                typeof(LookupCommitReason),
                typeof(LookupTextBox),
                new PropertyMetadata(LookupCommitReason.Programmatic));

        /// <summary>最近一次选择的提交来源的依赖属性标识。</summary>
        public static readonly DependencyProperty CommitReasonProperty = CommitReasonPropertyKey.DependencyProperty;

        /// <summary>最近一次选择的提交来源。</summary>
        public LookupCommitReason CommitReason
        {
            get => (LookupCommitReason)GetValue(CommitReasonProperty);
            private set => SetValue(CommitReasonPropertyKey, value);
        }

        /// <summary>用户显式提交候选后通知宿主；程序赋值不会触发。</summary>
        public event EventHandler<LookupSelectionCommittedEventArgs> SelectionCommitted;

        /// <summary>从候选提取选择值的属性名的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectedValuePathProperty =
            DependencyProperty.Register(
                nameof(SelectedValuePath),
                typeof(string),
                typeof(LookupTextBox),
                new PropertyMetadata(string.Empty));

        /// <summary>从候选提取选择值的属性名。</summary>
        public string SelectedValuePath
        {
            get => (string)GetValue(SelectedValuePathProperty);
            set => SetValue(SelectedValuePathProperty, value);
        }

        /// <summary>按 SelectedValuePath 提取的选择值，默认双向绑定的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectedValueProperty =
            DependencyProperty.Register(
                nameof(SelectedValue),
                typeof(object),
                typeof(LookupTextBox),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>按 SelectedValuePath 提取的选择值，默认双向绑定。</summary>
        public object SelectedValue
        {
            get => GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        /// <summary>候选弹层的开关状态的依赖属性标识。</summary>
        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register(
                nameof(IsDropDownOpen),
                typeof(bool),
                typeof(LookupTextBox),
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>候选弹层的开关状态。</summary>
        public bool IsDropDownOpen
        {
            get => (bool)GetValue(IsDropDownOpenProperty);
            set => SetValue(IsDropDownOpenProperty, value);
        }

        /// <summary>候选的可选显示模板的依赖属性标识。</summary>
        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(
                nameof(ItemTemplate),
                typeof(DataTemplate),
                typeof(LookupTextBox),
                new PropertyMetadata(null, OnItemTemplateChanged));

        /// <summary>候选的可选显示模板。</summary>
        public DataTemplate ItemTemplate
        {
            get => (DataTemplate)GetValue(ItemTemplateProperty);
            set => SetValue(ItemTemplateProperty, value);
        }

        /// <summary>替代默认列表的自定义候选视图的依赖属性标识。</summary>
        public static readonly DependencyProperty CustomDropDownContentProperty =
            DependencyProperty.Register(
                nameof(CustomDropDownContent),
                typeof(object),
                typeof(LookupTextBox),
                new PropertyMetadata(null, OnCustomDropDownContentChanged));

        /// <summary>获取或设置替代默认 ListBox 的下拉内容。</summary>
        public object CustomDropDownContent
        {
            get => GetValue(CustomDropDownContentProperty);
            set => SetValue(CustomDropDownContentProperty, value);
        }

        /// <summary>输入或选择是否存在，用于清除按钮显示的依赖属性标识。</summary>
        public static readonly DependencyProperty HasTextOrSelectionProperty =
            DependencyProperty.Register(
                nameof(HasTextOrSelection),
                typeof(bool),
                typeof(LookupTextBox),
                new PropertyMetadata(false));

        /// <summary>输入或选择是否存在，用于清除按钮显示。</summary>
        public bool HasTextOrSelection
        {
            get => (bool)GetValue(HasTextOrSelectionProperty);
            private set => SetValue(HasTextOrSelectionProperty, value);
        }

        /// <summary>当前控件独立持有的候选过滤视图。</summary>
        public ICollectionView FilteredView { get; private set; }

        /// <summary>将焦点投递到输入框；隐藏或卸载后丢弃排队操作。</summary>
        public void FocusEditor()
        {
            var version = _lifetimeVersion;
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (version != _lifetimeVersion || !IsLoaded || !IsVisible) return;
                    PrepareEditorForInput();
                    InputTextBox.Focus();
                    Keyboard.Focus(InputTextBox);
                    InputTextBox.SelectAll();
                }),
                DispatcherPriority.Input);
        }

        /// <summary>连接自定义候选导航接口，不把业务查询带入控件。</summary>
        internal void SetCustomResultHost(ILookupResultHost resultHost)
        {
            _customResultHost = resultHost;
            UpdateResultPresenter();
            RefreshResultHost();
        }

        /// <summary>供自定义候选视图复用统一的提交过程。</summary>
        internal void CommitExternalSelection(object item, LookupCommitReason reason) =>
            CommitSelection(item, reason);

        /// <summary>提示文字变化时更新空值显示。</summary>
        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LookupTextBox)d).UpdateTextVisualState();
        }

        /// <summary>将配置间隔同步到本控件计时器。</summary>
        private static void OnSearchDelayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LookupTextBox)d)._debounceTimer.Interval = (TimeSpan)e.NewValue;
        }

        /// <summary>同步程序赋值但不把它当成用户搜索意图。</summary>
        private static void OnTextChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LookupTextBox)d;
            if (control._isInternalChanging)
            {
                return;
            }

            control._isSelectionDisplayMode = false;
            control.SetInputTextSilently(e.NewValue?.ToString() ?? string.Empty);
            control.UpdateTextVisualState();
        }

        /// <summary>为每个控件创建独立过滤视图并切换集合订阅。</summary>
        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LookupTextBox)d;
            control.DetachCollectionChanged();

            if (e.NewValue is IEnumerable source)
            {
                var cvs = new CollectionViewSource { Source = source };
                control.FilteredView = cvs.View;
                control.FilteredView.Filter = control.FilterItem;
                control.ResultList.ItemsSource = control.FilteredView;
                control.ApplyItemTemplate();
                control.AttachCollectionChanged(source);
                control.UpdateDropDownAfterItemsChanged();
            }
            else
            {
                control.FilteredView = null;
                control.ResultList.ItemsSource = null;
                control.SetSelectedItemSilently(null);
                control.SetCurrentValue(SelectedValueProperty, null);
                control.SetDisplayText(string.Empty);
                control.SetCurrentValue(IsDropDownOpenProperty, false);
            }

            control.RefreshResultHost();
            control.UpdateTextVisualState();
        }

        /// <summary>显示属性变更时同步候选模板和已选文字。</summary>
        private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LookupTextBox)d;
            control.ApplyItemTemplate();
            control.UpdateDisplayTextFromSelectedItem();
        }

        /// <summary>显示外部选择，不触发提交命令或查询。</summary>
        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LookupTextBox)d;
            if (control._isInternalChanging)
            {
                return;
            }

            if (e.NewValue == null)
            {
                control.SetCurrentValue(SelectedValueProperty, null);
                control.SetDisplayText(string.Empty);
                control._isSelectionDisplayMode = false;
                control.UpdateTextVisualState();
                return;
            }

            control.SetCurrentValue(SelectedValueProperty, control.GetSelectedValue(e.NewValue));
            control.SetDisplayText(control.GetDisplayText(e.NewValue));
            control.ClearInputTextSilently();
            control._isSelectionDisplayMode = true;
            control.UpdateTextVisualState();
        }

        /// <summary>更新候选模板而不同时设置 DisplayMemberPath。</summary>
        private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LookupTextBox)d).ApplyItemTemplate();
        }

        /// <summary>同步替代候选视图的显示与数据。</summary>
        private static void OnCustomDropDownContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LookupTextBox)d;
            control.UpdateResultPresenter();
            control.RefreshResultHost();
        }

        /// <summary>恢复可重入的集合订阅，重载本地视图但不主动查询。</summary>
        private void LookupTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            _lifetimeVersion++;
            AttachCollectionChanged(ItemsSource);
            FilteredView?.Refresh();
            RefreshResultHost();
            _hasUserInputIntent = false;
            ApplyItemTemplate();
            UpdateDisplayTextFromSelectedItem();
            UpdateTextVisualState();

            var editingCell = FindVisualAncestor<DataGridCell>(this);
            if (editingCell?.IsEditing == true)
            {
                FocusEditor();
                return;
            }

            if (IsKeyboardFocusWithin || IsFocused)
            {
                FocusEditor();
            }
        }

        /// <summary>停止防抖、关闭浮层并作废排队焦点，解除集合订阅。</summary>
        private void LookupTextBox_Unloaded(object sender, RoutedEventArgs e)
        {
            _lifetimeVersion++;
            _debounceTimer.Stop();
            _hasUserInputIntent = false;
            SetCurrentValue(IsDropDownOpenProperty, false);
            DetachCollectionChanged();
        }

        /// <summary>页签内容保活但被隐藏时关闭浮层并停止远程搜索防抖，避免后台页继续触发查询。</summary>
        private void LookupTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool && (bool)e.NewValue)
            {
                return;
            }

            _lifetimeVersion++;
            _debounceTimer.Stop();
            _hasUserInputIntent = false;
            SetCurrentValue(IsDropDownOpenProperty, false);
        }

        /// <summary>用户显式清除后执行的命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandProperty =
            DependencyProperty.Register(
                nameof(SelectionClearedCommand),
                typeof(ICommand),
                typeof(LookupTextBox),
                new PropertyMetadata(null));

        /// <summary>用户显式清除后执行的命令。</summary>
        public ICommand SelectionClearedCommand
        {
            get => (ICommand)GetValue(SelectionClearedCommandProperty);
            set => SetValue(SelectionClearedCommandProperty, value);
        }

        /// <summary>清除命令参数的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandParameterProperty =
            DependencyProperty.Register(
                nameof(SelectionClearedCommandParameter),
                typeof(object),
                typeof(LookupTextBox),
                new PropertyMetadata(null));

        /// <summary>清除命令参数。</summary>
        public object SelectionClearedCommandParameter
        {
            get => GetValue(SelectionClearedCommandParameterProperty);
            set => SetValue(SelectionClearedCommandParameterProperty, value);
        }

        /// <summary>获得焦点本身不代表用户请求打开候选。</summary>
        private void InputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _hasUserInputIntent = false;
            UpdateTextVisualState();
        }

        /// <summary>区分程序同步与用户编辑，选择本地过滤或远程防抖。</summary>
        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInternalChanging)
            {
                return;
            }

            var newText = InputTextBox.Text ?? string.Empty;
            _isSelectionDisplayMode = false;
            _hasUserInputIntent = IsLoaded && InputTextBox.IsKeyboardFocusWithin;

            if (ClearSelectionOnEdit && SelectedItem != null)
            {
                SetSelectedItemSilently(null);
                SetCurrentValue(SelectedValueProperty, null);
                SetDisplayText(string.Empty);
            }

            SyncTextDependencySilently(newText);
            UpdateTextVisualState();

            if (!_hasUserInputIntent)
            {
                _debounceTimer.Stop();
                return;
            }

            if (string.IsNullOrWhiteSpace(newText))
            {
                _debounceTimer.Stop();
                FilteredView?.Refresh();
                RefreshResultHost();
                SetCurrentValue(IsDropDownOpenProperty, false);
                return;
            }

            if (ShouldUseLocalFilter())
            {
                _debounceTimer.Stop();
                if (!HasActiveUserQuery())
                {
                    SetCurrentValue(IsDropDownOpenProperty, false);
                    return;
                }

                ResolveSelectionByText(clearSelectionOnNoMatch: ClearSelectionOnEdit);
                RefreshAndUpdateDropDown();
            }
            else if (SearchMode == LookupTextBoxSearchMode.Remote && SearchCommand != null)
            {
                _debounceTimer.Stop();
                if (IsLoaded && InputTextBox.IsKeyboardFocusWithin)
                {
                    _debounceTimer.Start();
                }
            }
            else
            {
                _debounceTimer.Stop();
                OpenDropDownFromUserIntent(selectFirstItem: false);
            }
        }

        /// <summary>仅向仍可见且具有编辑意图的页面发送查询命令。</summary>
        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();

            if (!HasActiveUserQuery())
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                return;
            }

            if (!ShouldUseLocalFilter())
            {
                ExecuteSearchCommand();
                return;
            }

            ResolveSelectionByText(clearSelectionOnNoMatch: ClearSelectionOnEdit);
            RefreshAndUpdateDropDown();
        }

        /// <summary>清除输入和选择并通知页面，不触发远程搜索。</summary>
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _debounceTimer.Stop();
            _hasUserInputIntent = false;
            _isSelectionDisplayMode = false;
            SetSelectedItemSilently(null);
            SetCurrentValue(SelectedValueProperty, null);
            SetDisplayText(string.Empty);
            SetInputTextSilently(string.Empty);
            SyncTextDependencySilently(string.Empty);
            FilteredView?.Refresh();
            SetCurrentValue(IsDropDownOpenProperty, false);
            UpdateTextVisualState();
            InputTextBox.Focus();
            if (SelectionClearedCommand?.CanExecute(SelectionClearedCommandParameter) == true)
            {
                SelectionClearedCommand.Execute(SelectionClearedCommandParameter);
            }
        }

        /// <summary>记录按下前的弹层状态，避免外部关闭后的立即重开。</summary>
        private void BtnDropDown_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Popup.StaysOpen=false 可能在 Click 前先关闭弹层，按下时保存状态才能可靠实现切换。
            _dropDownWasOpenOnButtonPress = IsDropDownOpen;
        }

        /// <summary>协调 Popup 自动关闭和展开按钮点击顺序。</summary>
        private void DropdownPopup_Closed(object sender, EventArgs e)
        {
            // Popup 可能先于按钮的 PreviewMouseDown 处理外部点击；此时用鼠标状态阻止随后 Click 重新打开。
            if (Mouse.LeftButton == MouseButtonState.Pressed && BtnDropDown.IsMouseOver)
            {
                _dropDownWasOpenOnButtonPress = true;
            }
        }

        /// <summary>按显式用户操作切换候选层。</summary>
        private void BtnDropDown_Click(object sender, RoutedEventArgs e)
        {
            if (_dropDownWasOpenOnButtonPress || IsDropDownOpen)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
            }
            else
            {
                OpenDropDownFromUserIntent(selectFirstItem: false);
            }

            _dropDownWasOpenOnButtonPress = false;
        }

        /// <summary>处理候选导航、提交和取消；Tab 保留宿主正常导航。</summary>
        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && (IsDropDownOpen || IsEditingPreservedSelection()))
            {
                _debounceTimer.Stop();
                _hasUserInputIntent = false;
                SetCurrentValue(IsDropDownOpenProperty, false);
                RestorePreservedSelectionDisplay();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space && string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                OpenDropDownFromUserIntent(selectFirstItem: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && !IsDropDownOpen)
            {
                OpenDropDownFromUserIntent(selectFirstItem: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && (!IsDropDownOpen || !HasResultItems()))
            {
                ExecuteQueryCommand();
                e.Handled = true;
                return;
            }

            if (!IsDropDownOpen || !HasResultItems())
            {
                return;
            }

            if (e.Key == Key.Down)
            {
                MoveResultSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveResultSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                var currentItem = GetCurrentResultItem();
                if (currentItem != null)
                {
                    CommitSelection(currentItem, e.Key == Key.Tab ? LookupCommitReason.Tab : LookupCommitReason.Enter);
                }
                else if (e.Key == Key.Enter)
                {
                    ExecuteQueryCommand();
                }
                e.Handled = e.Key != Key.Tab;
            }
        }

        /// <summary>离开整个 Lookup 后停止查询，并按匹配规则收束输入。</summary>
        private void InputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (IsFocusWithinLookup(e.NewFocus as DependencyObject))
            {
                return;
            }

            var wasDropDownOpen = IsDropDownOpen;
            _debounceTimer.Stop();
            _hasUserInputIntent = false;
            SetCurrentValue(IsDropDownOpenProperty, false);

            if (RestorePreservedSelectionDisplay())
            {
                return;
            }

            if (!RequireItemMatch || wasDropDownOpen)
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
                SetCurrentValue(SelectedValueProperty, null);
                SetDisplayText(string.Empty);
            }

            UpdateTextVisualState();
        }

        /// <summary>将鼠标点击的实际候选提交给绑定与命令。</summary>
        private void ResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;
            if (originalSource == null)
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

        /// <summary>从查看态转入编辑态并交还输入焦点。</summary>
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

        /// <summary>在当前加载生命周期内更新候选，忽略已解绑数据源通知。</summary>
        private void ItemsSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ItemsSource_CollectionChanged(sender, e)));
                return;
            }

            if (!IsLoaded || !ReferenceEquals(sender, _itemsCollectionChangedSource)) return;
            UpdateDropDownAfterItemsChanged();
        }

        /// <summary>按配置字段进行本地文本片段匹配。</summary>
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
                if (LookupTextMatcher.IsMatch(value, input))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>保留双向绑定地发布候选、选择值和提交事件，并关闭弹层。</summary>
        private void CommitSelection(object item, LookupCommitReason reason)
        {
            if (item == null)
            {
                return;
            }

            _debounceTimer.Stop();
            _hasUserInputIntent = false;
            SetSelectedItemSilently(item);
            SetCurrentValue(SelectedValueProperty, GetSelectedValue(item));
            SetDisplayText(GetDisplayText(item));
            ClearInputTextSilently();
            _isSelectionDisplayMode = true;
            SetCurrentValue(IsDropDownOpenProperty, false);
            CommitReason = reason;
            UpdateTextVisualState();
            ExecuteSelectionCommittedCommand(item, reason);
            SelectionCommitted?.Invoke(
                this,
                new LookupSelectionCommittedEventArgs(item, SelectedValue, Text, reason));
        }

        /// <summary>按显示字段或匹配字段精确查找，按配置保留未匹配的原选择。</summary>
        private bool ResolveSelectionByText(bool clearSelectionOnNoMatch = true)
        {
            var text = (InputTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || ItemsSource == null)
            {
                return string.IsNullOrWhiteSpace(text);
            }

            object customItem;
            if (_customResultHost != null && _customResultHost.TryResolveExact(text, out customItem) && customItem != null)
            {
                SetSelectedItemSilently(customItem);
                SetCurrentValue(SelectedValueProperty, GetSelectedValue(customItem));
                SetDisplayText(GetDisplayText(customItem));
                CommitReason = LookupCommitReason.ExactMatch;
                return true;
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
                        SetCurrentValue(SelectedValueProperty, GetSelectedValue(item));
                        SetDisplayText(GetDisplayText(item));
                        CommitReason = LookupCommitReason.ExactMatch;
                        return true;
                    }
                }
            }

            if (clearSelectionOnNoMatch)
            {
                SetSelectedItemSilently(null);
                SetCurrentValue(SelectedValueProperty, null);
                SetDisplayText(string.Empty);
            }

            return false;
        }

        /// <summary>进入编辑状态并选中现有显示文本。</summary>
        private void BeginEditSelection()
        {
            PrepareEditorForInput();
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        /// <summary>为直接编辑准备文本，按 ClearSelectionOnEdit 决定原选择的保留。</summary>
        private void PrepareEditorForInput()
        {
            _debounceTimer.Stop();
            _hasUserInputIntent = false;

            var initialText = _isSelectionDisplayMode
                ? DisplayTextBlock.Text ?? string.Empty
                : InputTextBox.Text ?? Text ?? string.Empty;

            _isSelectionDisplayMode = false;
            if (ClearSelectionOnEdit && SelectedItem != null)
            {
                SetSelectedItemSilently(null);
                SetCurrentValue(SelectedValueProperty, null);
                SetDisplayText(string.Empty);
            }

            SetInputTextSilently(initialText);
            SetCurrentValue(IsDropDownOpenProperty, false);
            UpdateTextVisualState();
        }

        /// <summary>判断当前是否正在编辑一个尚未替换的原选择。</summary>
        private bool IsEditingPreservedSelection()
        {
            return !ClearSelectionOnEdit && !_isSelectionDisplayMode && SelectedItem != null;
        }

        /// <summary>取消编辑时恢复原选择显示，不发送提交命令。</summary>
        private bool RestorePreservedSelectionDisplay()
        {
            if (!IsEditingPreservedSelection())
            {
                return false;
            }

            ClearInputTextSilently();
            SyncTextDependencySilently(string.Empty);
            SetDisplayText(GetDisplayText(SelectedItem));
            _isSelectionDisplayMode = true;
            FilteredView?.Refresh();
            UpdateTextVisualState();
            return true;
        }

        /// <summary>在候选提交后执行宿主命令，默认参数为选中对象。</summary>
        private void ExecuteSelectionCommittedCommand(object item, LookupCommitReason reason)
        {
            var command = SelectionCommittedCommand;
            var parameter = SelectionCommittedCommandParameter ?? item;

            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }
        }

        /// <summary>验证最短输入和焦点后发送远程搜索意图。</summary>
        private void ExecuteSearchCommand()
        {
            if (SearchMode == LookupTextBoxSearchMode.Remote &&
                (!IsLoaded || !IsVisible || !InputTextBox.IsKeyboardFocusWithin))
            {
                return;
            }

            var text = (InputTextBox.Text ?? string.Empty).Trim();
            if (text.Length < MinimumSearchLength)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                return;
            }

            if (SearchCommand?.CanExecute(text) == true)
            {
                SearchCommand.Execute(text);
            }
        }

        /// <summary>将无候选情况下的 Enter 交给页面查询命令。</summary>
        private void ExecuteQueryCommand()
        {
            var text = (InputTextBox.Text ?? string.Empty).Trim();
            if (QueryCommand?.CanExecute(text) == true)
            {
                QueryCommand.Execute(text);
            }
        }

        /// <summary>互斥设置候选显示模板和显示字段。</summary>
        private void ApplyItemTemplate()
        {
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

        /// <summary>刷新当前候选并根据是否存在结果决定弹层状态。</summary>
        private void RefreshAndUpdateDropDown()
        {
            if (_customResultHost != null)
            {
                RefreshResultHost();
                SetCurrentValue(IsDropDownOpenProperty, _customResultHost.HasItems);
                return;
            }

            if (FilteredView == null)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                return;
            }

            FilteredView.Refresh();
            SetCurrentValue(IsDropDownOpenProperty, !FilteredView.IsEmpty);
        }

        /// <summary>仅响应用户展开意图，可定位到第一候选。</summary>
        private bool OpenDropDownFromUserIntent(bool selectFirstItem)
        {
            if (_customResultHost != null)
            {
                RefreshResultHost();
                if (!_customResultHost.HasItems)
                {
                    SetCurrentValue(IsDropDownOpenProperty, false);
                    return false;
                }

                if (selectFirstItem)
                {
                    _customResultHost.SelectFirst();
                }

                SetCurrentValue(IsDropDownOpenProperty, true);
                return true;
            }

            if (FilteredView == null)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                return false;
            }

            FilteredView.Refresh();
            if (FilteredView.IsEmpty)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                return false;
            }

            if (selectFirstItem)
            {
                ResultList.SelectedIndex = 0;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
            }

            SetCurrentValue(IsDropDownOpenProperty, true);
            return true;
        }

        /// <summary>数据到达时保持原展开意图，初始化不自动弹出。</summary>
        private void UpdateDropDownAfterItemsChanged()
        {
            if (_customResultHost != null)
            {
                RefreshResultHost();
                if (IsDropDownOpen || HasActiveUserQuery())
                {
                    SetCurrentValue(IsDropDownOpenProperty, _customResultHost.HasItems);
                }

                return;
            }

            if (FilteredView == null)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                return;
            }

            FilteredView.Refresh();
            if (IsDropDownOpen || HasActiveUserQuery())
            {
                SetCurrentValue(IsDropDownOpenProperty, !FilteredView.IsEmpty);
            }
        }

        /// <summary>确认控件仍可见、聚焦且达到最小有效输入长度。</summary>
        private bool HasActiveUserQuery()
        {
            if (!_hasUserInputIntent || !IsLoaded || !IsVisible || !InputTextBox.IsKeyboardFocusWithin)
            {
                return false;
            }

            var input = (InputTextBox.Text ?? string.Empty).Trim();
            return input.Length >= Math.Max(1, MinimumSearchLength);
        }

        /// <summary>从已选择对象重建只读显示文字。</summary>
        private void UpdateDisplayTextFromSelectedItem()
        {
            SetDisplayText(SelectedItem == null ? string.Empty : GetDisplayText(SelectedItem));
        }

        /// <summary>更新查看态文字并同步三种文本显示状态。</summary>
        private void SetDisplayText(string text)
        {
            DisplayTextBlock.Text = text ?? string.Empty;
            UpdateTextVisualState();
        }

        /// <summary>协调编辑、选择显示、占位文本和清除按钮。</summary>
        private void UpdateTextVisualState()
        {
            var hasInput = !string.IsNullOrWhiteSpace(InputTextBox.Text);
            var hasDisplay = !string.IsNullOrWhiteSpace(DisplayTextBlock.Text);

            if (_isSelectionDisplayMode && hasDisplay)
            {
                InputTextBox.Visibility = Visibility.Collapsed;
                DisplayTextBlockBorder.Visibility = Visibility.Visible;
                PlaceholderTextBlock.Visibility = Visibility.Collapsed;
                HasTextOrSelection = true;
                return;
            }

            InputTextBox.Visibility = Visibility.Visible;
            DisplayTextBlockBorder.Visibility = Visibility.Collapsed;
            PlaceholderTextBlock.Visibility = hasInput || hasDisplay ? Visibility.Collapsed : Visibility.Visible;
            HasTextOrSelection = hasInput || hasDisplay;
        }

        /// <summary>按模式和 Text 绑定决定本地过滤职责。</summary>
        private bool ShouldUseLocalFilter()
        {
            if (SearchMode == LookupTextBoxSearchMode.Local)
            {
                return true;
            }

            if (SearchMode == LookupTextBoxSearchMode.Remote)
            {
                return false;
            }

            var bindingExpression = BindingOperations.GetBindingExpression(this, TextProperty);
            return bindingExpression == null;
        }

        /// <summary>读取配置的显示属性，未配置时使用对象字符串。</summary>
        private string GetDisplayText(object item)
        {
            if (!string.IsNullOrWhiteSpace(DisplayMemberPath))
            {
                var prop = GetCachedProperty(item.GetType(), DisplayMemberPath);
                return prop?.GetValue(item)?.ToString() ?? string.Empty;
            }

            return item.ToString() ?? string.Empty;
        }

        /// <summary>提取绑定需要的主键或候选对象。</summary>
        private object GetSelectedValue(object item)
        {
            if (string.IsNullOrWhiteSpace(SelectedValuePath))
            {
                return item;
            }

            var prop = GetCachedProperty(item.GetType(), SelectedValuePath);
            return prop?.GetValue(item);
        }

        /// <summary>读取一个匹配字段的文本。</summary>
        private string GetMemberText(object item, string memberPath)
        {
            if (string.IsNullOrWhiteSpace(memberPath))
            {
                return item.ToString() ?? string.Empty;
            }

            var prop = GetCachedProperty(item.GetType(), memberPath);
            return prop?.GetValue(item)?.ToString() ?? string.Empty;
        }

        /// <summary>缓存属性查找结果，避免每次输入重复反射。</summary>
        private PropertyInfo GetCachedProperty(Type type, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
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

        /// <summary>整理不重复的匹配字段列表。</summary>
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

            return !string.IsNullOrWhiteSpace(DisplayMemberPath)
                ? new List<string> { DisplayMemberPath }
                : new List<string> { string.Empty };
        }

        /// <summary>精确匹配时优先采用显示字段。</summary>
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

        /// <summary>沿可视树寻找鼠标点击所属的候选容器。</summary>
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

        /// <summary>同步选择且保留绑定，避免递归触发显示同步。</summary>
        private void SetSelectedItemSilently(object item)
        {
            if (Equals(SelectedItem, item))
            {
                return;
            }

            _isInternalChanging = true;
            try
            {
                SetCurrentValue(SelectedItemProperty, item);
            }
            finally
            {
                _isInternalChanging = false;
            }
        }

        /// <summary>同步内部输入框而不产生用户搜索意图。</summary>
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

        /// <summary>在进入查看态时清空内部输入。</summary>
        private void ClearInputTextSilently()
        {
            SetInputTextSilently(string.Empty);
        }

        /// <summary>更新外部搜索文本并保留原绑定。</summary>
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
                SetCurrentValue(TextProperty, target);
            }
            finally
            {
                _isInternalChanging = false;
            }
        }

        /// <summary>幂等连接弱集合事件，避免缓存单例留住已关闭控件。</summary>
        private void AttachCollectionChanged(IEnumerable source)
        {
            DetachCollectionChanged();
            _itemsCollectionChangedSource = source as INotifyCollectionChanged;
            if (_itemsCollectionChangedSource != null)
            {
                CollectionChangedEventManager.AddHandler(_itemsCollectionChangedSource, ItemsSource_CollectionChanged);
            }
        }

        /// <summary>对称移除集合订阅。</summary>
        private void DetachCollectionChanged()
        {
            if (_itemsCollectionChangedSource != null)
            {
                CollectionChangedEventManager.RemoveHandler(_itemsCollectionChangedSource, ItemsSource_CollectionChanged);
                _itemsCollectionChangedSource = null;
            }
        }

        /// <summary>识别原生 DataGrid 编辑宿主，不依赖 EnterpriseGrid。</summary>
        private static T FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
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

        /// <summary>互斥显示默认列表和自定义候选视图。</summary>
        private void UpdateResultPresenter()
        {
            if (ResultList == null || CustomDropDownPresenter == null)
            {
                return;
            }

            var hasCustomContent = CustomDropDownContent != null;
            ResultList.Visibility = hasCustomContent ? Visibility.Collapsed : Visibility.Visible;
            CustomDropDownPresenter.Visibility = hasCustomContent ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>把本地候选与文本转交自定义视图。</summary>
        private void RefreshResultHost() =>
            _customResultHost?.Refresh(ItemsSource, InputTextBox?.Text ?? Text ?? string.Empty);

        /// <summary>取得当前候选宿主是否有可用结果。</summary>
        private bool HasResultItems() =>
            _customResultHost?.HasItems ?? ResultList.Items.Count > 0;

        /// <summary>取得当前键盘导航所指向的候选。</summary>
        private object GetCurrentResultItem() =>
            _customResultHost?.CurrentItem ?? ResultList.SelectedItem;

        /// <summary>按方向移动并限制到合法候选索引。</summary>
        private void MoveResultSelection(int offset)
        {
            if (_customResultHost != null)
            {
                _customResultHost.MoveSelection(offset);
                return;
            }

            ResultList.SelectedIndex = Math.Max(0, Math.Min(
                ResultList.SelectedIndex + offset,
                Math.Max(0, ResultList.Items.Count - 1)));
            ResultList.ScrollIntoView(ResultList.SelectedItem);
        }

        /// <summary>将独立 Popup 可视树纳入控件焦点边界。</summary>
        private bool IsFocusWithinLookup(DependencyObject focusedElement)
        {
            if (focusedElement == null)
            {
                return false;
            }

            return IsDescendantOrSelf(focusedElement, this)
                   || DropdownPopup.Child is DependencyObject popupChild
                   && IsDescendantOrSelf(focusedElement, popupChild);
        }

        /// <summary>判断焦点是否仍位于控件或候选弹层内部。</summary>
        private static bool IsDescendantOrSelf(DependencyObject current, DependencyObject ancestor)
        {
            for (DependencyObject node = current; node != null; node = GetParent(node))
            {
                if (ReferenceEquals(node, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>兼容可视对象与逻辑内容的父节点查找。</summary>
        private static DependencyObject GetParent(DependencyObject current)
        {
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }

            return LogicalTreeHelper.GetParent(current);
        }
    }

}
