using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.Customers;

namespace SwiftExport.Client.VSTO.Features.Customers.Components
{

    /// <summary>基于客户缓存按客户编码执行本地筛选，并提交完整客户 DTO。</summary>
    public partial class CustomerAutoSuggestComboBox : UserControl
    {
        /// <summary>初始化客户本地缓存选择控件。</summary>
        public CustomerAutoSuggestComboBox() => InitializeComponent();

        /// <summary>页面提供的候选集合的依赖属性标识。</summary>
        public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
        /// <summary>当前客户 DTO的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectedCustomerProperty = RegisterTwoWay(nameof(SelectedCustomer), typeof(CustomerDto));
        /// <summary>确认选择后执行的页面命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
        /// <summary>确认选择命令的可选上下文的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
        /// <summary>显式清除后执行的页面命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
        /// <summary>清除命令的可选上下文的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
        /// <summary>未选择时的提示文字的依赖属性标识。</summary>
        public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索客户编码");

        /// <summary>获取或设置完整客户缓存。</summary>
        public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
        /// <summary>获取或设置当前确认的完整客户 DTO。</summary>
        public CustomerDto SelectedCustomer { get => (CustomerDto)GetValue(SelectedCustomerProperty); set => SetValue(SelectedCustomerProperty, value); }
        /// <summary>获取或设置选择确认命令；未指定参数时传递完整 DTO。</summary>
        public ICommand SelectionCommittedCommand { get => (ICommand)GetValue(SelectionCommittedCommandProperty); set => SetValue(SelectionCommittedCommandProperty, value); }
        /// <summary>获取或设置选择确认命令的业务上下文。</summary>
        public object SelectionCommittedCommandParameter { get => GetValue(SelectionCommittedCommandParameterProperty); set => SetValue(SelectionCommittedCommandParameterProperty, value); }
        /// <summary>获取或设置显式清空选择后的命令。</summary>
        public ICommand SelectionClearedCommand { get => (ICommand)GetValue(SelectionClearedCommandProperty); set => SetValue(SelectionClearedCommandProperty, value); }
        /// <summary>获取或设置清空命令的业务上下文。</summary>
        public object SelectionClearedCommandParameter { get => GetValue(SelectionClearedCommandParameterProperty); set => SetValue(SelectionClearedCommandParameterProperty, value); }
        /// <summary>获取或设置空选择提示文字。</summary>
        public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

        /// <summary>注册由页面提供的普通绑定属性。</summary>
        private static DependencyProperty Register(string name, Type type, object defaultValue = null) =>
            DependencyProperty.Register(name, type, typeof(CustomerAutoSuggestComboBox), new PropertyMetadata(defaultValue));

        /// <summary>选择变更默认回写页面绑定。</summary>
        private static DependencyProperty RegisterTwoWay(string name, Type type) =>
            DependencyProperty.Register(name, type, typeof(CustomerAutoSuggestComboBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    }

}
