using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExport.VSTO.UserControls
{
    public partial class PaginationControl : UserControl
    {
        public int[] PageSizeOptions { get; } = new int[] { 20, 50, 100, 200 };
        public PaginationControl()
        {
            // 绑定内部命令，每次翻页后都要重新计算状态，并向外抛出事件
            FirstPageCommand = new RelayCommand_Internal(() => { PageIndex = 1; RaisePageChanged(); });
            PrevPageCommand = new RelayCommand_Internal(() => { if (PageIndex > 1) PageIndex--; RaisePageChanged(); });
            NextPageCommand = new RelayCommand_Internal(() => { if (PageIndex < TotalPages) PageIndex++; RaisePageChanged(); });
            LastPageCommand = new RelayCommand_Internal(() => { PageIndex = TotalPages; RaisePageChanged(); });

            InitializeComponent();
        }

        // ================== 1. 输入参数 (允许外部双向绑定) ==================

        public static readonly DependencyProperty PageIndexProperty =
            DependencyProperty.Register("PageIndex", typeof(int), typeof(PaginationControl), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPagePropsChanged));
        public int PageIndex { get => (int)GetValue(PageIndexProperty); set => SetValue(PageIndexProperty, value); }

        public static readonly DependencyProperty PageSizeProperty =
            DependencyProperty.Register("PageSize", typeof(int), typeof(PaginationControl), new FrameworkPropertyMetadata(50, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPageSizeChanged));
        public int PageSize { get => (int)GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }

        public static readonly DependencyProperty TotalCountProperty =
            DependencyProperty.Register("TotalCount", typeof(int), typeof(PaginationControl), new PropertyMetadata(0, OnPagePropsChanged));
        public int TotalCount { get => (int)GetValue(TotalCountProperty); set => SetValue(TotalCountProperty, value); }

        public static readonly DependencyProperty PageChangedCommandProperty =
            DependencyProperty.Register("PageChangedCommand", typeof(ICommand), typeof(PaginationControl), new PropertyMetadata(null));
        public ICommand PageChangedCommand { get => (ICommand)GetValue(PageChangedCommandProperty); set => SetValue(PageChangedCommandProperty, value); }


        // ================== 2. 内部状态 (升级为 DP，彻底解决只读报错！) ==================

        public static readonly DependencyProperty TotalPagesProperty =
            DependencyProperty.Register("TotalPages", typeof(int), typeof(PaginationControl), new PropertyMetadata(1));
        public int TotalPages { get => (int)GetValue(TotalPagesProperty); private set => SetValue(TotalPagesProperty, value); }

        public static readonly DependencyProperty CanGoBackProperty =
            DependencyProperty.Register("CanGoBack", typeof(bool), typeof(PaginationControl), new PropertyMetadata(false));
        public bool CanGoBack { get => (bool)GetValue(CanGoBackProperty); private set => SetValue(CanGoBackProperty, value); }

        public static readonly DependencyProperty CanGoForwardProperty =
            DependencyProperty.Register("CanGoForward", typeof(bool), typeof(PaginationControl), new PropertyMetadata(false));
        public bool CanGoForward { get => (bool)GetValue(CanGoForwardProperty); private set => SetValue(CanGoForwardProperty, value); }

        public static readonly DependencyProperty PageNumbersProperty =
            DependencyProperty.Register("PageNumbers", typeof(IEnumerable<int>), typeof(PaginationControl), new PropertyMetadata(new List<int> { 1 }));
        public IEnumerable<int> PageNumbers { get => (IEnumerable<int>)GetValue(PageNumbersProperty); private set => SetValue(PageNumbersProperty, value); }

        // ================== 3. 核心计算逻辑 ==================

        private static void OnPagePropsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PaginationControl control) control.RefreshInternalState();
        }

        private static void OnPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PaginationControl control)
            {
                control.PageIndex = 1; // 切换条数时，防呆设计：强制回到第一页
                control.RefreshInternalState();
                control.RaisePageChanged();
            }
        }

        // ★ 核心发动机：每次数据变化，重新计算所有状态并赋值给 DP
        private void RefreshInternalState()
        {
            TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
            CanGoBack = PageIndex > 1;
            CanGoForward = PageIndex < TotalPages && TotalPages > 0;

            // ★ 新增：每次总页数重新计算时，刷新下拉框里的数字列表
            // 例如 TotalPages 是 5，这里就会生成 [1, 2, 3, 4, 5]
            PageNumbers = Enumerable.Range(1, TotalPages).ToList();
        }

        private void RaisePageChanged()
        {
            RefreshInternalState();
            if (PageChangedCommand != null && PageChangedCommand.CanExecute(null))
            {
                PageChangedCommand.Execute(null);
            }
        }


        public ICommand FirstPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LastPageCommand { get; }
    }

    // 内部极简命令引擎
    internal class RelayCommand_Internal : ICommand
    {
        private readonly Action _execute;
        public RelayCommand_Internal(Action execute) => _execute = execute;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }






}