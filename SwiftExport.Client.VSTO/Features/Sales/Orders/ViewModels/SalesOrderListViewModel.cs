using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels
{
    /// <summary>侧栏只读销售列表，复用管理页搜索；负责分页、勾选及会话隔离，不访问 Excel COM。</summary>
    public sealed class SalesOrderListViewModel : ViewModelBaseEx
    {
        private const int PageSize = 30;
        private readonly ISalesOrderManagerApi api;
        private readonly SessionService session;
        private readonly CurrentUserService user;
        private readonly IUiDispatcher dispatcher;
        private readonly ApiCallRunner errors;
        private CancellationTokenSource query;
        private long sessionVersion;
        private long? initializedVersion;
        private string keyword = "", customerCode = "", statusText = "点击查询加载销售订单。";
        private string outputPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private int confirmation = -1, pageIndex = 1, totalCount;
        private SalesOrderListRow selectedOrder;

        /// <summary>用户输入的管理列表关键词，发送时裁剪空白。</summary>
        public string Keyword { get { return keyword; } set { SetProperty(ref keyword, value); } }
        /// <summary>单个客户代码筛选，空值表示全部客户。</summary>
        public string CustomerCode { get { return customerCode; } set { SetProperty(ref customerCode, value); } }
        /// <summary>确认筛选的页面值，-1 表示全部，其余对应契约枚举。</summary>
        public int Confirmation { get { return confirmation; } set { SetProperty(ref confirmation, value); } }
        /// <summary>预留 Excel 输出目录，默认本机桌面；本期不创建文件。</summary>
        public string OutputPath { get { return outputPath; } set { SetProperty(ref outputPath, value); } }
        /// <summary>DataGrid 当前行，为后续单张生成预留，不自动勾选。</summary>
        public SalesOrderListRow SelectedOrder { get { return selectedOrder; } set { SetProperty(ref selectedOrder, value); } }
        /// <summary>显示已接纳的查询范围与账号变更提示。</summary>
        public string StatusText { get { return statusText; } private set { SetProperty(ref statusText, value); } }
        public ObservableCollection<SalesOrderListRow> Orders { get; } = new ObservableCollection<SalesOrderListRow>();
        /// <summary>仅计算当前页显式勾选数，不推断其他页选中情况。</summary>
        public int CheckedCount => Orders.Count(x => x.IsChecked);
        /// <summary>已成功加载的页码和查询总页数。</summary>
        public string PageStatus => "第 " + pageIndex + " / " + Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize)) + " 页";
        /// <summary>页面与命令共享查看权限；服务器仍是最终授权入口。</summary>
        public bool CanBrowse => !IsDisposed && !IsBusy && user.HasPermission(PermissionKeys.SalesOrderView);
        public IReadOnlyList<KeyValuePair<int, string>> ConfirmationOptions { get; } = new[]
        {
            new KeyValuePair<int, string>(-1, "全部"), new KeyValuePair<int, string>(0, "待确认"),
            new KeyValuePair<int, string>(1, "已确认")
        };
        public IAsyncRelayCommand SearchCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand PreviousPageCommand { get; }
        public IAsyncRelayCommand NextPageCommand { get; }

        /// <summary>仅登记依赖与命令；首次显示才请求搜索，不在 Excel 启动时预热。</summary>
        public SalesOrderListViewModel(ISalesOrderManagerApi api, SessionService session, CurrentUserService user,
            IUiDispatcher dispatcher, ApiCallRunner errors)
        {
            this.api = api; this.session = session; this.user = user; this.dispatcher = dispatcher; this.errors = errors;
            Title = "销售订单"; sessionVersion = session.Capture().Version;
            SearchCommand = new AsyncRelayCommand(() => LoadAsync(1), () => CanBrowse);
            RefreshCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex), () => CanBrowse);
            PreviousPageCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex - 1), () => CanBrowse && pageIndex > 1);
            NextPageCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex + 1), () => CanBrowse && (long)pageIndex * PageSize < totalCount);
            RegisterCommand(SearchCommand); RegisterCommand(RefreshCommand);
            RegisterCommand(PreviousPageCommand); RegisterCommand(NextPageCommand);
            session.Changed += OnSessionChanged;
        }

        /// <summary>同会话重复显示不重置查询和勾选；失败可用查询或刷新显式重试。</summary>
        public async Task InitializeAsync()
        {
            if (!CanBrowse || initializedVersion == sessionVersion) return;
            initializedVersion = sessionVersion;
            await LoadAsync(1);
        }

        // 每次操作冻结筛选条件；身份变化或 Scope 释放取消传输，并使旧响应无法接纳。
        private async Task LoadAsync(int targetPage)
        {
            if (!CanBrowse) return;
            var request = new SalesOrderManagerSearchRequest
            {
                Keyword = (Keyword ?? "").Trim(),
                CustomerCodes = string.IsNullOrWhiteSpace(CustomerCode) ? new List<string>() : new List<string> { CustomerCode.Trim() },
                ConfirmationStatuses = Confirmation < 0 ? new List<ConfirmationStatusEnum>() : new List<ConfirmationStatusEnum> { (ConfirmationStatusEnum)Confirmation },
                PageIndex = Math.Max(1, targetPage), PageSize = PageSize
            };
            var operation = new CancellationTokenSource();
            var version = session.Capture().Version;
            query = operation; IsBusy = true; OnPropertyChanged(nameof(CanBrowse)); ErrorMessage = "";
            try
            {
                var result = await api.SearchAsync(request, operation.Token);
                if (!CanAccept(operation, version)) return;
                if (result == null || !result.Success || result.Data == null)
                {
                    ErrorMessage = result?.Error?.Message ?? "查询销售订单失败，请重试。";
                    return;
                }
                ClearOrders();
                foreach (var order in result.Data.Items)
                {
                    var row = new SalesOrderListRow(order); row.PropertyChanged += OnRowChanged; Orders.Add(row);
                }
                totalCount = result.Data.TotalCount; pageIndex = request.PageIndex;
                StatusText = "共 " + totalCount + " 张，本页 " + Orders.Count + " 张；勾选仅限本页。";
                OnPropertyChanged(nameof(PageStatus)); OnPropertyChanged(nameof(CheckedCount));
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            catch (Exception ex) { if (CanAccept(operation, version)) ErrorMessage = errors.Describe(ex); }
            finally
            {
                if (ReferenceEquals(query, operation))
                {
                    query = null; IsBusy = false; OnPropertyChanged(nameof(CanBrowse)); RefreshCommands();
                }
                operation.Dispose();
            }
        }

        private bool CanAccept(CancellationTokenSource operation, long version) => !IsDisposed
            && !operation.IsCancellationRequested && ReferenceEquals(query, operation) && session.Capture().Version == version;
        // 只监听勾选属性，不把选择状态写入 Contracts DTO。
        private void OnRowChanged(object sender, PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(SalesOrderListRow.IsChecked)) OnPropertyChanged(nameof(CheckedCount)); }
        private void ClearOrders()
        {
            foreach (var row in Orders) row.PropertyChanged -= OnRowChanged;
            Orders.Clear(); SelectedOrder = null; OnPropertyChanged(nameof(CheckedCount));
        }
        // 事件可由 HTTP 线程触发；清空旧账号筛选、行及勾选后，等待用户首次显示或主动查询。
        private async void OnSessionChanged(object sender, EventArgs args)
        {
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (IsDisposed || sessionVersion == session.Capture().Version) return;
                    sessionVersion = session.Capture().Version; initializedVersion = null;
                    query?.Cancel(); query = null; IsBusy = false;
                    ClearOrders(); Keyword = ""; CustomerCode = ""; Confirmation = -1;
                    pageIndex = 1; totalCount = 0; ErrorMessage = "";
                    StatusText = "账号已变更，请重新查询销售订单。";
                    OnPropertyChanged(nameof(PageStatus)); OnPropertyChanged(nameof(CanBrowse)); RefreshCommands();
                });
            }
            catch (OperationCanceledException) { /* 宿主已退出，不再通知页面。 */ }
        }
        /// <summary>销毁 Scope 时终止查询、解绑会话及行事件；隐藏窗格不触发释放。</summary>
        protected override void DisposeCore()
        {
            session.Changed -= OnSessionChanged;
            query?.Cancel(); query = null; ClearOrders();
        }
    }
}
