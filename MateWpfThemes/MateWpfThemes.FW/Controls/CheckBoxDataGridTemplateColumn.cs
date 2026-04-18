using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Controls
{
    /// <summary>
    /// 可复用的复选框 DataGrid 列，支持全选/反选功能
    /// 提供灵活的绑定配置，避免在 XAML 中重复定义复选框列模板
    /// </summary>
    public class CheckBoxDataGridTemplateColumn : DataGridTemplateColumn
    {
        /// <summary>
        /// DataGrid 全选属性的名称（在 DataGrid 的 DataContext 上）
        /// </summary>
        public static readonly DependencyProperty AllCheckedPropertyNameProperty =
            DependencyProperty.Register(
                nameof(AllCheckedPropertyName),
                typeof(string),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata("IsAllChecked", OnPropertyChanged));

        /// <summary>
        /// 单行复选框属性的名称（在行数据对象上）
        /// </summary>
        public static readonly DependencyProperty ItemCheckedPropertyNameProperty =
            DependencyProperty.Register(
                nameof(ItemCheckedPropertyName),
                typeof(string),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata("IsChecked", OnPropertyChanged));

        /// <summary>
        /// 单行复选框改变时执行的命令名称（在 DataGrid 的 DataContext 上）
        /// </summary>
        public static readonly DependencyProperty ItemCheckedCommandNameProperty =
            DependencyProperty.Register(
                nameof(ItemCheckedCommandName),
                typeof(string),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata("ItemCheckedChangedCommand", OnPropertyChanged));

        /// <summary>
        /// 头部复选框的提示文本
        /// </summary>
        public static readonly DependencyProperty HeaderCheckBoxToolTipProperty =
            DependencyProperty.Register(
                nameof(HeaderCheckBoxToolTip),
                typeof(string),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata("全选/全不选"));

        /// <summary>
        /// 表头复选框的外边距。
        /// 当主题重写了 CheckBox 默认模板后，可通过这里微调表头与单元格的视觉对齐。
        /// </summary>
        public static readonly DependencyProperty HeaderCheckBoxMarginProperty =
            DependencyProperty.Register(
                nameof(HeaderCheckBoxMargin),
                typeof(Thickness),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(new Thickness(-9), OnPropertyChanged));

        /// <summary>
        /// 单元格复选框的外边距。
        /// 用于修正行内复选框在不同主题样式下的偏移问题。
        /// </summary>
        public static readonly DependencyProperty CellCheckBoxMarginProperty =
            DependencyProperty.Register(
                nameof(CellCheckBoxMargin),
                typeof(Thickness),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(new Thickness(0), OnPropertyChanged));

        /// <summary>
        /// 表头复选框的宽度。
        /// 当主题样式让复选框实际占位变宽时，可通过这里显式统一宽度。
        /// </summary>
        public static readonly DependencyProperty HeaderCheckBoxWidthProperty =
            DependencyProperty.Register(
                nameof(HeaderCheckBoxWidth),
                typeof(double),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(double.NaN, OnPropertyChanged));

        /// <summary>
        /// 单元格复选框的宽度。
        /// 用于和表头复选框保持同一视觉宽度，避免出现不对齐。
        /// </summary>
        public static readonly DependencyProperty CellCheckBoxWidthProperty =
            DependencyProperty.Register(
                nameof(CellCheckBoxWidth),
                typeof(double),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(double.NaN, OnPropertyChanged));

        /// <summary>
        /// 表头复选框的水平对齐方式。
        /// 当列头内容区域较宽时，可通过这里控制其左对齐、居中或右对齐。
        /// </summary>
        public static readonly DependencyProperty HeaderCheckBoxHorizontalAlignmentProperty =
            DependencyProperty.Register(
                nameof(HeaderCheckBoxHorizontalAlignment),
                typeof(HorizontalAlignment),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(HorizontalAlignment.Center, OnPropertyChanged));

        /// <summary>
        /// 表头复选框的垂直对齐方式。
        /// </summary>
        public static readonly DependencyProperty HeaderCheckBoxVerticalAlignmentProperty =
            DependencyProperty.Register(
                nameof(HeaderCheckBoxVerticalAlignment),
                typeof(VerticalAlignment),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(VerticalAlignment.Center, OnPropertyChanged));

        /// <summary>
        /// 单元格复选框的水平对齐方式。
        /// 用于修正行内复选框在不同主题模板下的定位。
        /// </summary>
        public static readonly DependencyProperty CellCheckBoxHorizontalAlignmentProperty =
            DependencyProperty.Register(
                nameof(CellCheckBoxHorizontalAlignment),
                typeof(HorizontalAlignment),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(HorizontalAlignment.Center, OnPropertyChanged));

        /// <summary>
        /// 单元格复选框的垂直对齐方式。
        /// </summary>
        public static readonly DependencyProperty CellCheckBoxVerticalAlignmentProperty =
            DependencyProperty.Register(
                nameof(CellCheckBoxVerticalAlignment),
                typeof(VerticalAlignment),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(VerticalAlignment.Center, OnPropertyChanged));

        /// <summary>
        /// 表头复选框的样式。
        /// 这里仍然要求是 CheckBox 样式，但其模板完全可以做成 ToggleButton/ToggleSwitch 的视觉效果。
        /// 若未显式传入，则回退到主题或应用程序默认的 CheckBox 样式。
        /// </summary>
        public static readonly DependencyProperty HeaderCheckBoxStyleProperty =
            DependencyProperty.Register(
                nameof(HeaderCheckBoxStyle),
                typeof(Style),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(null, OnPropertyChanged));

        /// <summary>
        /// 单元格复选框的样式。
        /// 可用于将行内复选框渲染成开关、按钮式切换等外观。
        /// 若未显式传入，则回退到主题或应用程序默认的 CheckBox 样式。
        /// </summary>
        public static readonly DependencyProperty CellCheckBoxStyleProperty =
            DependencyProperty.Register(
                nameof(CellCheckBoxStyle),
                typeof(Style),
                typeof(CheckBoxDataGridTemplateColumn),
                new PropertyMetadata(null, OnPropertyChanged));

        public string AllCheckedPropertyName
        {
            get => (string)GetValue(AllCheckedPropertyNameProperty);
            set => SetValue(AllCheckedPropertyNameProperty, value);
        }

        public string ItemCheckedPropertyName
        {
            get => (string)GetValue(ItemCheckedPropertyNameProperty);
            set => SetValue(ItemCheckedPropertyNameProperty, value);
        }

        public string ItemCheckedCommandName
        {
            get => (string)GetValue(ItemCheckedCommandNameProperty);
            set => SetValue(ItemCheckedCommandNameProperty, value);
        }

        public string HeaderCheckBoxToolTip
        {
            get => (string)GetValue(HeaderCheckBoxToolTipProperty);
            set => SetValue(HeaderCheckBoxToolTipProperty, value);
        }

        public Thickness HeaderCheckBoxMargin
        {
            get => (Thickness)GetValue(HeaderCheckBoxMarginProperty);
            set => SetValue(HeaderCheckBoxMarginProperty, value);
        }

        public Thickness CellCheckBoxMargin
        {
            get => (Thickness)GetValue(CellCheckBoxMarginProperty);
            set => SetValue(CellCheckBoxMarginProperty, value);
        }

        public double HeaderCheckBoxWidth
        {
            get => (double)GetValue(HeaderCheckBoxWidthProperty);
            set => SetValue(HeaderCheckBoxWidthProperty, value);
        }

        public double CellCheckBoxWidth
        {
            get => (double)GetValue(CellCheckBoxWidthProperty);
            set => SetValue(CellCheckBoxWidthProperty, value);
        }

        public HorizontalAlignment HeaderCheckBoxHorizontalAlignment
        {
            get => (HorizontalAlignment)GetValue(HeaderCheckBoxHorizontalAlignmentProperty);
            set => SetValue(HeaderCheckBoxHorizontalAlignmentProperty, value);
        }

        public VerticalAlignment HeaderCheckBoxVerticalAlignment
        {
            get => (VerticalAlignment)GetValue(HeaderCheckBoxVerticalAlignmentProperty);
            set => SetValue(HeaderCheckBoxVerticalAlignmentProperty, value);
        }

        public HorizontalAlignment CellCheckBoxHorizontalAlignment
        {
            get => (HorizontalAlignment)GetValue(CellCheckBoxHorizontalAlignmentProperty);
            set => SetValue(CellCheckBoxHorizontalAlignmentProperty, value);
        }

        public VerticalAlignment CellCheckBoxVerticalAlignment
        {
            get => (VerticalAlignment)GetValue(CellCheckBoxVerticalAlignmentProperty);
            set => SetValue(CellCheckBoxVerticalAlignmentProperty, value);
        }

        public Style HeaderCheckBoxStyle
        {
            get => (Style)GetValue(HeaderCheckBoxStyleProperty);
            set => SetValue(HeaderCheckBoxStyleProperty, value);
        }

        public Style CellCheckBoxStyle
        {
            get => (Style)GetValue(CellCheckBoxStyleProperty);
            set => SetValue(CellCheckBoxStyleProperty, value);
        }

        public CheckBoxDataGridTemplateColumn()
        {
            // 设置列的基本属性
            Width = 40;
            CanUserResize = false;
            CanUserReorder = false;
            CanUserSort = false;

            // 初始化模板
            RefreshTemplates();
        }

        /// <summary>
        /// 属性改变时重新生成模板
        /// </summary>
        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CheckBoxDataGridTemplateColumn column)
            {
                column.RefreshTemplates();
            }
        }

        /// <summary>
        /// 刷新头部和单元格模板
        /// </summary>
        private void RefreshTemplates()
        {
            HeaderTemplate = CreateHeaderTemplate();
            CellTemplate = CreateCellTemplate();
        }

        /// <summary>
        /// 创建头部复选框模板（全选/全不选）
        /// </summary>
        private DataTemplate CreateHeaderTemplate()
        {
            var template = new DataTemplate();
            var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));

            // 绑定复选框的选中状态到 DataGrid DataContext 的属性
            checkBoxFactory.SetBinding(CheckBox.IsCheckedProperty,
                new Binding($"DataContext.{AllCheckedPropertyName}")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            checkBoxFactory.SetValue(CheckBox.ToolTipProperty, HeaderCheckBoxToolTip);
            // 仅在外部显式传入样式时才设置本地 Style，避免覆盖主题默认的 CheckBox 样式
            if (HeaderCheckBoxStyle != null)
            {
                checkBoxFactory.SetValue(FrameworkElement.StyleProperty, HeaderCheckBoxStyle);
            }
            checkBoxFactory.SetValue(FrameworkElement.MarginProperty, HeaderCheckBoxMargin);
            checkBoxFactory.SetValue(FrameworkElement.WidthProperty, HeaderCheckBoxWidth);
            checkBoxFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HeaderCheckBoxHorizontalAlignment);
            checkBoxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, HeaderCheckBoxVerticalAlignment);

            template.VisualTree = checkBoxFactory;
            template.Seal();
            return template;
        }

        /// <summary>
        /// 创建单元格复选框模板（行选择）
        /// </summary>
        private DataTemplate CreateCellTemplate()
        {
            var template = new DataTemplate();
            var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));

            // 绑定复选框的选中状态到行数据的属性
            checkBoxFactory.SetBinding(CheckBox.IsCheckedProperty,
                new Binding(ItemCheckedPropertyName)
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            // 绑定复选框的命令
            checkBoxFactory.SetBinding(CheckBox.CommandProperty,
                new Binding($"DataContext.{ItemCheckedCommandName}")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(UserControl), 1)
                });

            // 仅在外部显式传入样式时才设置本地 Style，避免覆盖主题默认的 CheckBox 样式
            if (CellCheckBoxStyle != null)
            {
                checkBoxFactory.SetValue(FrameworkElement.StyleProperty, CellCheckBoxStyle);
            }
            checkBoxFactory.SetValue(FrameworkElement.MarginProperty, CellCheckBoxMargin);
            checkBoxFactory.SetValue(FrameworkElement.WidthProperty, CellCheckBoxWidth);
            checkBoxFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, CellCheckBoxHorizontalAlignment);
            checkBoxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, CellCheckBoxVerticalAlignment);

            template.VisualTree = checkBoxFactory;
            template.Seal();
            return template;
        }
    }
}
