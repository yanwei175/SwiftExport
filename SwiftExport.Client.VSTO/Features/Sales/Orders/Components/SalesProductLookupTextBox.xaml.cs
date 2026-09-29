using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Components
{

    /// <summary>
    /// 销售订单行使用的产品远程搜索编辑器，封装客户产品候选的显示与选择语义。
    /// </summary>
    public partial class SalesProductLookupTextBox : UserControl
    {
        /// <summary>创建产品候选编辑器，不在控件中启动查询。</summary>
        public SalesProductLookupTextBox()
        {
            InitializeComponent();
        }

        /// <summary>搜索输入文字的依赖属性标识。</summary>
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SalesProductLookupTextBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>搜索输入文字。</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>页面提供的候选集合的依赖属性标识。</summary>
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SalesProductLookupTextBox),
            new PropertyMetadata(null));

        /// <summary>页面提供的候选集合。</summary>
        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>当前销售产品候选 DTO的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectedProductProperty = DependencyProperty.Register(
            nameof(SelectedProduct),
            typeof(SalesOrderProductLookupDto),
            typeof(SalesProductLookupTextBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>当前销售产品候选 DTO。</summary>
        public SalesOrderProductLookupDto SelectedProduct
        {
            get => (SalesOrderProductLookupDto)GetValue(SelectedProductProperty);
            set => SetValue(SelectedProductProperty, value);
        }

        /// <summary>当前候选的 ProductId的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectedProductIdProperty = DependencyProperty.Register(
            nameof(SelectedProductId),
            typeof(Guid?),
            typeof(SalesProductLookupTextBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>当前候选的 ProductId。</summary>
        public Guid? SelectedProductId
        {
            get => (Guid?)GetValue(SelectedProductIdProperty);
            set => SetValue(SelectedProductIdProperty, value);
        }

        /// <summary>页面负责执行的远程搜索命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SearchCommandProperty = DependencyProperty.Register(
            nameof(SearchCommand), typeof(ICommand), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

        /// <summary>页面负责执行的远程搜索命令。</summary>
        public ICommand SearchCommand
        {
            get => (ICommand)GetValue(SearchCommandProperty);
            set => SetValue(SearchCommandProperty, value);
        }

        /// <summary>确认选择后执行的页面命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandProperty = DependencyProperty.Register(
            nameof(SelectionCommittedCommand), typeof(ICommand), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

        /// <summary>确认选择后执行的页面命令。</summary>
        public ICommand SelectionCommittedCommand
        {
            get => (ICommand)GetValue(SelectionCommittedCommandProperty);
            set => SetValue(SelectionCommittedCommandProperty, value);
        }

        /// <summary>确认选择命令的可选上下文的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = DependencyProperty.Register(
            nameof(SelectionCommittedCommandParameter), typeof(object), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

        /// <summary>确认选择命令的可选上下文。</summary>
        public object SelectionCommittedCommandParameter
        {
            get => GetValue(SelectionCommittedCommandParameterProperty);
            set => SetValue(SelectionCommittedCommandParameterProperty, value);
        }

        /// <summary>显式清除后执行的页面命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandProperty = DependencyProperty.Register(
            nameof(SelectionClearedCommand), typeof(ICommand), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

        /// <summary>显式清除后执行的页面命令。</summary>
        public ICommand SelectionClearedCommand
        {
            get => (ICommand)GetValue(SelectionClearedCommandProperty);
            set => SetValue(SelectionClearedCommandProperty, value);
        }

        /// <summary>清除命令的可选上下文的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandParameterProperty = DependencyProperty.Register(
            nameof(SelectionClearedCommandParameter), typeof(object), typeof(SalesProductLookupTextBox), new PropertyMetadata(null));

        /// <summary>清除命令的可选上下文。</summary>
        public object SelectionClearedCommandParameter
        {
            get => GetValue(SelectionClearedCommandParameterProperty);
            set => SetValue(SelectionClearedCommandParameterProperty, value);
        }

        /// <summary>查询进行中的显示状态的依赖属性标识。</summary>
        public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
            nameof(IsBusy), typeof(bool), typeof(SalesProductLookupTextBox), new PropertyMetadata(false));

        /// <summary>查询进行中的显示状态。</summary>
        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }

        /// <summary>远程输入防抖间隔的依赖属性标识。</summary>
        public static readonly DependencyProperty SearchDelayProperty = DependencyProperty.Register(
            nameof(SearchDelay),
            typeof(TimeSpan),
            typeof(SalesProductLookupTextBox),
            new PropertyMetadata(TimeSpan.FromMilliseconds(300)),
            value => value is TimeSpan delay && delay >= TimeSpan.Zero);

        /// <summary>远程输入防抖间隔。</summary>
        public TimeSpan SearchDelay
        {
            get => (TimeSpan)GetValue(SearchDelayProperty);
            set => SetValue(SearchDelayProperty, value);
        }

        /// <summary>触发搜索的最短字符数的依赖属性标识。</summary>
        public static readonly DependencyProperty MinimumSearchLengthProperty = DependencyProperty.Register(
            nameof(MinimumSearchLength),
            typeof(int),
            typeof(SalesProductLookupTextBox),
            new PropertyMetadata(1, null, (_, value) => Math.Max(0, (int)value)));

        /// <summary>触发搜索的最短字符数。</summary>
        public int MinimumSearchLength
        {
            get => (int)GetValue(MinimumSearchLengthProperty);
            set => SetValue(MinimumSearchLengthProperty, value);
        }

        /// <summary>未选择时的提示文字的依赖属性标识。</summary>
        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(SalesProductLookupTextBox),
            new PropertyMetadata("搜索 SKU、客户型号或描述"));

        /// <summary>未选择时的提示文字。</summary>
        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        /// <summary>将焦点交给内部编辑器，卸载后由其丢弃排队操作。</summary>
        public void FocusEditor() => LookupEditor.FocusEditor();
    }

}
