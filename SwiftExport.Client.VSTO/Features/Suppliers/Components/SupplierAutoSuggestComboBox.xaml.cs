using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExport.Client.VSTO.Features.Suppliers.Components
{
    /// <summary>使用页面提供的供应商缓存按编码本地筛选，并提交完整供应商 DTO。</summary>
    public partial class SupplierAutoSuggestComboBox : UserControl
    {
        /// <summary>初始化供应商选择控件；控件自身不触发缓存加载。</summary>
        public SupplierAutoSuggestComboBox() => InitializeComponent();

        /// <summary>候选集合的依赖属性标识。</summary>
        public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
        /// <summary>当前供应商的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectedSupplierProperty = RegisterTwoWay(nameof(SelectedSupplier), typeof(SupplierDto));
        /// <summary>确认选择命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
        /// <summary>确认选择命令参数的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
        /// <summary>清除选择命令的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
        /// <summary>清除选择命令参数的依赖属性标识。</summary>
        public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
        /// <summary>提示文字的依赖属性标识。</summary>
        public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索供应商编码");
        /// <summary>填满宿主高度的依赖属性标识。</summary>
        public static readonly DependencyProperty StretchToHostProperty = Register(nameof(StretchToHost), typeof(bool), false);

        /// <summary>获取或设置页面传入的本地供应商缓存集合。</summary>
        public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
        /// <summary>获取或设置已确认的完整供应商 DTO，默认双向回写。</summary>
        public SupplierDto SelectedSupplier { get => (SupplierDto)GetValue(SelectedSupplierProperty); set => SetValue(SelectedSupplierProperty, value); }
        /// <summary>获取或设置确认命令；未提供参数时传递完整供应商 DTO。</summary>
        public ICommand SelectionCommittedCommand { get => (ICommand)GetValue(SelectionCommittedCommandProperty); set => SetValue(SelectionCommittedCommandProperty, value); }
        /// <summary>获取或设置确认命令的业务上下文。</summary>
        public object SelectionCommittedCommandParameter { get => GetValue(SelectionCommittedCommandParameterProperty); set => SetValue(SelectionCommittedCommandParameterProperty, value); }
        /// <summary>获取或设置清除命令。</summary>
        public ICommand SelectionClearedCommand { get => (ICommand)GetValue(SelectionClearedCommandProperty); set => SetValue(SelectionClearedCommandProperty, value); }
        /// <summary>获取或设置清除命令的业务上下文。</summary>
        public object SelectionClearedCommandParameter { get => GetValue(SelectionClearedCommandParameterProperty); set => SetValue(SelectionClearedCommandParameterProperty, value); }
        /// <summary>获取或设置空选择提示文字。</summary>
        public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
        /// <summary>获取或设置编辑器是否填满宿主高度。</summary>
        public bool StretchToHost { get => (bool)GetValue(StretchToHostProperty); set => SetValue(StretchToHostProperty, value); }

        /// <summary>注册控件公开的普通绑定属性。</summary>
        private static DependencyProperty Register(string name, Type type, object defaultValue = null) =>
            DependencyProperty.Register(name, type, typeof(SupplierAutoSuggestComboBox), new PropertyMetadata(defaultValue));

        /// <summary>选择变更默认回写页面绑定。</summary>
        private static DependencyProperty RegisterTwoWay(string name, Type type) =>
            DependencyProperty.Register(name, type, typeof(SupplierAutoSuggestComboBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    }
}
