using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.CustomerViews.Messages;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.CustomerViews.ViewModels
{
    /// <summary>
    /// 客户选择对话框 ViewModel。
    /// 用于单据场景中选择客户，并返回完整客户 DTO 供后续“带出更多信息”使用。
    /// </summary>
    public class CustomerSelectorDialogViewModel : ViewModelBaseEx
    {
        private readonly ICustomerApi _customerApi;
        private CancellationTokenSource _searchCts;

        private int _currentPage = 1;
        private readonly int _pageSize = 50;
        private bool _hasMoreData = true;

        /// <summary>
        /// 通知 UI 在重置查询后滚动到顶部。
        /// 由 View 代码后台订阅并执行实际滚动。
        /// </summary>
        public event Action RequestScrollToTop;

        #region 命令

        /// <summary>
        /// 加载客户数据命令（支持滚动触底增量加载）。
        /// </summary>
        public RelayCommand LoadDataCommand { get; }

        /// <summary>
        /// 确认选择命令。
        /// </summary>
        public RelayCommand ConfirmCommand { get; }

        /// <summary>
        /// 取消命令。
        /// </summary>
        public RelayCommand CancelCommand { get; }

        #endregion

        public CustomerSelectorDialogViewModel(ICustomerApi customerApi)
        {
            _customerApi = customerApi;

            SourceCustomers = new ObservableCollection<CustomerDto>();
            StatusMessage = "准备就绪";

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync(false));
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(Cancel);

            // 中文注释：初始化时先加载第一页数据，用户打开对话框即可直接选择。
            _ = LoadDataAsync(true);
        }

        #region 数据源属性

        private ObservableCollection<CustomerDto> _sourceCustomers;
        /// <summary>
        /// 左侧客户数据源。
        /// </summary>
        public ObservableCollection<CustomerDto> SourceCustomers
        {
            get => _sourceCustomers;
            set => SetProperty(ref _sourceCustomers, value);
        }

        private CustomerDto _selectedCustomer;
        /// <summary>
        /// 当前选中的客户（单选）。
        /// </summary>
        public CustomerDto SelectedCustomer
        {
            get => _selectedCustomer;
            set => SetProperty(ref _selectedCustomer, value);
        }

        #endregion

        #region 搜索条件

        private string _searchText;
        /// <summary>
        /// 关键字搜索（CustomerCode/名称/联系人）。
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    TriggerSearch();
                }
            }
        }

        private string _batchSearchText;
        /// <summary>
        /// 批量搜索（多行文本，每行一项）。
        /// </summary>
        public string BatchSearchText
        {
            get => _batchSearchText;
            set
            {
                if (SetProperty(ref _batchSearchText, value))
                {
                    TriggerSearch();
                }
            }
        }

        private string _statusMessage;
        /// <summary>
        /// 窗口底部状态信息。
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        #endregion

        #region 核心查询逻辑

        /// <summary>
        /// 触发防抖查询，避免用户输入时频繁请求后端接口。
        /// </summary>
        private void TriggerSearch()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            Task.Delay(400, _searchCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                // 中文注释：防抖结束后统一切回 UI 线程发起查询，确保集合更新线程安全。
                DispatcherProvider.Invoke(() =>
                {
                    _ = LoadDataAsync(true);
                });
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// 加载客户列表数据。
        /// resetPage=true 时表示重置查询条件并从第一页加载；否则执行下一页增量加载。
        /// </summary>
        private async Task LoadDataAsync(bool resetPage)
        {
            // 中文注释：防止并发重入，避免重复请求或分页状态混乱。
            if (IsBusy) return;

            if (resetPage)
            {
                DispatcherProvider.Invoke(() =>
                {
                    _currentPage = 1;
                    _hasMoreData = true;
                    SourceCustomers.Clear();
                    SelectedCustomer = null;
                    RequestScrollToTop?.Invoke();
                });
            }

            if (!_hasMoreData)
            {
                return;
            }

            StatusMessage = "正在加载客户数据...";

            using (BusyScope())
            {
                try
                {
                    var request = new PagedRequestDto
                    {
                        SearchText = SearchText,
                        BatchSearchTexts = BatchSearchText,
                        PageIndex = _currentPage,
                        PageSize = _pageSize
                    };

                    var result = await _customerApi.GetPagedAsync(request);
                    if (result != null && result.Success && result.Data != null)
                    {
                        var items = result.Data.Items?.ToList() ?? new List<CustomerDto>();

                        DispatcherProvider.Invoke(() =>
                        {
                            foreach (var item in items)
                            {
                                SourceCustomers.Add(item);
                            }
                        });

                        _hasMoreData = items.Count == _pageSize;
                        if (_hasMoreData)
                        {
                            _currentPage++;
                        }

                        StatusMessage = $"已加载 {SourceCustomers.Count} 条客户数据";
                    }
                    else
                    {
                        StatusMessage = $"加载失败: {result?.Message}";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"加载异常: {ex.Message}";
                }
            }
        }

        #endregion

        #region 确认与取消

        /// <summary>
        /// 确认选择客户。
        /// 若用户未主动选中任何行，则默认取当前列表第一条，提高录入效率。
        /// </summary>
        private void Confirm()
        {
            if (SelectedCustomer == null)
            {
                if (SourceCustomers.Count > 0)
                {
                    SelectedCustomer = SourceCustomers.First();
                }
                else
                {
                    StatusMessage = "没有可确认的客户数据";
                    return;
                }
            }

            // 中文注释：通过 Messenger 广播结果，以当前 ViewModel 作为 token 隔离多窗口实例。
            Messenger.Send(new CustomerSelectionResultMessage(SelectedCustomer), this);
        }

        /// <summary>
        /// 取消客户选择。
        /// </summary>
        private void Cancel()
        {
            Messenger.Send(new CustomerSelectionResultMessage(null), this);
        }

        #endregion
    }
}
