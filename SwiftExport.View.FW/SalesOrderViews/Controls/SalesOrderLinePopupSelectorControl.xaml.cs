using Mapster;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.View.FW.DI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.SalesOrderViews.Controls
{
    /// <summary>
    /// 客户产品选择 Popup 控件。
    /// 内部自带搜索框与数据获取逻辑，对外通过 SubmitCommand 回传选择结果。
    /// </summary>
    public partial class SalesOrderLinePopupSelectorControl : UserControl
    {
        private readonly DispatcherTimer _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        private readonly ISalesOrderApi _salesOrderApi;

        public SalesOrderLinePopupSelectorControl()
        {
            InitializeComponent();
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            _salesOrderApi = IocProvider.GetService<ISalesOrderApi>();
        }


        #region 对外依赖属性

        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(false, OnIsOpenChanged));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        public static readonly DependencyProperty PopupPlacementTargetProperty =
            DependencyProperty.Register(nameof(PopupPlacementTarget), typeof(UIElement), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(null));

        public UIElement PopupPlacementTarget
        {
            get => (UIElement)GetValue(PopupPlacementTargetProperty);
            set => SetValue(PopupPlacementTargetProperty, value);
        }

        public static readonly DependencyProperty RowContextProperty =
            DependencyProperty.Register(nameof(RowContext), typeof(object), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(null));

        public object RowContext
        {
            get => GetValue(RowContextProperty);
            set => SetValue(RowContextProperty, value);
        }

        public static readonly DependencyProperty CustomerIdProperty =
            DependencyProperty.Register(nameof(CustomerId), typeof(int), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(0));

        public int CustomerId
        {
            get => (int)GetValue(CustomerIdProperty);
            set => SetValue(CustomerIdProperty, value);
        }

        public static readonly DependencyProperty CustomerCodeProperty =
            DependencyProperty.Register(nameof(CustomerCode), typeof(string), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(string.Empty));

        public string CustomerCode
        {
            get => (string)GetValue(CustomerCodeProperty);
            set => SetValue(CustomerCodeProperty, value);
        }

        public static readonly DependencyProperty SubmitCommandProperty =
            DependencyProperty.Register(nameof(SubmitCommand), typeof(ICommand), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(null));

        public ICommand SubmitCommand
        {
            get => (ICommand)GetValue(SubmitCommandProperty);
            set => SetValue(SubmitCommandProperty, value);
        }

        #endregion

        #region 内部状态属性

        public ObservableCollection<SalesOrderLineDto> Items { get; } = new ObservableCollection<SalesOrderLineDto>();

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(SalesOrderLineDto), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(null));

        public SalesOrderLineDto SelectedItem
        {
            get => (SalesOrderLineDto)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(SalesOrderLinePopupSelectorControl),
                new PropertyMetadata(string.Empty, OnSearchTextChanged));

        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        #endregion

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as SalesOrderLinePopupSelectorControl;
            if (control == null) return;

            if ((bool)e.NewValue)
            {
                // 中文注释：每次打开先清空旧结果，避免误选上一轮数据。
                control.Items.Clear();
                control.SelectedItem = null;
            }
        }

        private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as SalesOrderLinePopupSelectorControl;
            if (control == null) return;

            control._searchDebounceTimer.Stop();
            control._searchDebounceTimer.Start();
        }

        private void SelectorPopup_Opened(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SearchTextBox.Focus();
                Keyboard.Focus(SearchTextBox);
                SearchTextBox.SelectAll();
            }), DispatcherPriority.Background);
        }

        private async void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            await LoadItemsAsync(SearchText);
        }

        private async Task LoadItemsAsync(string keyword)
        {
            if (!IsOpen)
                return;

            if (_salesOrderApi == null)
                return;

            if (CustomerId <= 0)
            {
                Items.Clear();
                SelectedItem = null;
                return;
            }

            if (string.IsNullOrWhiteSpace(keyword))
            {
                Items.Clear();
                SelectedItem = null;
                return;
            }

            try
            {
                var searchDto = new SalesProductSearchDto
                {
                    CustomerId = CustomerId,
                    PageIndex = 1,
                    PageSize = 20
                };

                var result = await _salesOrderApi.GetSalesProductPagedListAsync(searchDto);
                Items.Clear();

                if (result?.Success == true && result.Data?.Items != null)
                {
                    foreach (var dto in result.Data.Items)
                        Items.Add(dto.Adapt<SalesOrderLineDto>());

                    SelectedItem = Items.FirstOrDefault();
                }
                else
                {
                    SelectedItem = null;
                }
            }
            catch
            {
                Items.Clear();
                SelectedItem = null;
            }
        }

        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsOpen) return;

            if (e.Key == Key.Down)
            {
                MoveSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                SubmitCurrent();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                IsOpen = false;
                e.Handled = true;
            }
        }

        private void MoveSelection(int step)
        {
            if (Items.Count == 0) return;

            var index = ResultListBox.SelectedIndex;
            if (index < 0)
                index = step > 0 ? 0 : Items.Count - 1;
            else
            {
                index += step;
                if (index < 0) index = 0;
                if (index >= Items.Count) index = Items.Count - 1;
            }

            ResultListBox.SelectedIndex = index;
            ResultListBox.ScrollIntoView(ResultListBox.SelectedItem);
        }

        private void ResultListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsOpen) return;
            SubmitCurrent();
        }

        private void SubmitCurrent()
        {
            if (SelectedItem == null)
            {
                if (Items.Count > 0)
                    SelectedItem = Items[0];
                else
                    return;
            }

            var args = new SalesOrderLinePopupSelectorSubmitArgs
            {
                RowContext = RowContext,
                SelectedItem = SelectedItem
            };

            if (SubmitCommand?.CanExecute(args) == true)
            {
                SubmitCommand.Execute(args);
            }

            IsOpen = false;
        }
    }
}
