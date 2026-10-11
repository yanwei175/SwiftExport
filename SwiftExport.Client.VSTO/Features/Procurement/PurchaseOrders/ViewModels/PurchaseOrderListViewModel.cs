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
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Services.OrderExport;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels
{
    /// <summary>侧栏只读采购列表，复用管理页搜索；负责分页、勾选及会话隔离，不访问 Excel COM。</summary>
    public sealed class PurchaseOrderListViewModel : ViewModelBaseEx
    {
        private const int PageSize = 30;
        private readonly IPurchaseOrderManagerApi api;
        private readonly SessionService session;
        private readonly CurrentUserService user;
        private readonly IUiDispatcher dispatcher;
        private readonly ApiCallRunner errors;
        private readonly IPurchaseOrderExportService exporter;
        private CancellationTokenSource query;
        private CancellationTokenSource export;
        private string exportReport = "";
        private long sessionVersion;
        private long? initializedVersion;
        private string keyword = "", customerCode = "", statusText = "点击查询加载采购订单。";
        private string outputPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private int confirmation = -1, pageIndex = 1, totalCount;
        private PurchaseDueViewEnum quickView;
        private PurchaseOrderListRow selectedOrder;

        /// <summary>用户输入的管理列表关键词，发送时裁剪空白。</summary>
        public string Keyword { get { return keyword; } set { SetProperty(ref keyword, value); } }
        /// <summary>单个客户代码筛选，空值表示全部客户。</summary>
        public string CustomerCode { get { return customerCode; } set { SetProperty(ref customerCode, value); } }
        /// <summary>确认筛选的页面值，-1 表示全部，其余对应契约枚举。</summary>
        public int Confirmation { get { return confirmation; } set { SetProperty(ref confirmation, value); } }
        /// <summary>复用服务端快捷视图，与其余条件取交集。</summary>
        public PurchaseDueViewEnum QuickView { get { return quickView; } set { SetProperty(ref quickView, value); } }
        /// <summary>导出目标文件夹，默认桌面；一次导出冻结点击时的目录。</summary>
        public string OutputPath { get { return outputPath; } set { if (SetProperty(ref outputPath, value)) RefreshCommands(); } }
        /// <summary>单张导出使用的当前行，独立于批量勾选状态。</summary>
        public PurchaseOrderListRow SelectedOrder { get { return selectedOrder; } set { if (SetProperty(ref selectedOrder, value)) RefreshCommands(); } }
        /// <summary>导出结果，包括成功路径、缺失快照提示及各订单失败说明。</summary>
        public string ExportReport { get { return exportReport; } private set { SetProperty(ref exportReport, value); } }
        /// <summary>控制导出期间输出路径只读，避免界面目录与已冻结目录混淆。</summary>
        public bool IsExporting => export != null;
        /// <summary>显示已接纳的查询范围与账号变更提示。</summary>
        public string StatusText { get { return statusText; } private set { SetProperty(ref statusText, value); } }
        public ObservableCollection<PurchaseOrderListRow> Orders { get; } = new ObservableCollection<PurchaseOrderListRow>();
        /// <summary>仅计算当前页显式勾选数，不推断其他页选中情况。</summary>
        public int CheckedCount => Orders.Count(x => x.IsChecked);
        /// <summary>已成功加载的页码和查询总页数。</summary>
        public string PageStatus => "第 " + pageIndex + " / " + Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize)) + " 页";
        /// <summary>页面与命令共享查看权限；服务器仍是最终授权入口。</summary>
        public bool CanBrowse => !IsDisposed && !IsBusy && user.HasPermission(PermissionKeys.ProcurementOrderRead);
        public IReadOnlyList<KeyValuePair<int, string>> ConfirmationOptions { get; } = new[]
        {
            new KeyValuePair<int, string>(-1, "全部"), new KeyValuePair<int, string>(0, "待确认"),
            new KeyValuePair<int, string>(1, "已确认")
        };
        public IReadOnlyList<KeyValuePair<PurchaseDueViewEnum, string>> QuickViewOptions { get; } = new[]
        {
            new KeyValuePair<PurchaseDueViewEnum, string>(PurchaseDueViewEnum.All, "全部"),
            new KeyValuePair<PurchaseDueViewEnum, string>(PurchaseDueViewEnum.OverdueNotReady, "逾期未货好"),
            new KeyValuePair<PurchaseDueViewEnum, string>(PurchaseDueViewEnum.DueSoonNotReady, "临期未货好")
        };
        public IAsyncRelayCommand SearchCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand PreviousPageCommand { get; }
        public IAsyncRelayCommand NextPageCommand { get; }
        /// <summary>生成当前高亮订单，使用完整详情及固定采购模板。</summary>
        public IAsyncRelayCommand ExportSelectedCommand { get; }
        /// <summary>仅导出当前页显式勾选的订单，一张订单一个文件。</summary>
        public IAsyncRelayCommand ExportCheckedCommand { get; }

        /// <summary>仅登记依赖与命令；首次显示才请求搜索，不在 Excel 启动时预热。</summary>
        public PurchaseOrderListViewModel(IPurchaseOrderManagerApi api, SessionService session, CurrentUserService user,
            IUiDispatcher dispatcher, ApiCallRunner errors, IPurchaseOrderExportService exporter)
        {
            this.api = api; this.session = session; this.user = user; this.dispatcher = dispatcher; this.errors = errors;
            this.exporter = exporter;
            Title = "采购订单"; sessionVersion = session.Capture().Version;
            SearchCommand = new AsyncRelayCommand(() => LoadAsync(1), () => CanBrowse);
            RefreshCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex), () => CanBrowse);
            PreviousPageCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex - 1), () => CanBrowse && pageIndex > 1);
            NextPageCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex + 1), () => CanBrowse && (long)pageIndex * PageSize < totalCount);
            RegisterCommand(SearchCommand); RegisterCommand(RefreshCommand);
            RegisterCommand(PreviousPageCommand); RegisterCommand(NextPageCommand);
            ExportSelectedCommand = new AsyncRelayCommand(() => ExportAsync(false), () => CanBrowse && SelectedOrder != null
                && Orders.Contains(SelectedOrder) && !string.IsNullOrWhiteSpace(OutputPath));
            ExportCheckedCommand = new AsyncRelayCommand(() => ExportAsync(true), () => CanBrowse && CheckedCount > 0 && !string.IsNullOrWhiteSpace(OutputPath));
            RegisterCommand(ExportSelectedCommand); RegisterCommand(ExportCheckedCommand);
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
            var request = new PurchaseOrderManagerSearchRequest
            {
                Keyword = (Keyword ?? "").Trim(),
                CustomerCode = string.IsNullOrWhiteSpace(CustomerCode) ? null : CustomerCode.Trim(),
                ConfirmationStatuses = Confirmation < 0 ? new List<ConfirmationStatusEnum>() : new List<ConfirmationStatusEnum> { (ConfirmationStatusEnum)Confirmation },
                DueView = QuickView, PageIndex = Math.Max(1, targetPage), PageSize = PageSize
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
                    ErrorMessage = result?.Error?.Message ?? "查询采购订单失败，请重试。";
                    return;
                }
                ClearOrders();
                foreach (var order in result.Data.Items)
                {
                    var row = new PurchaseOrderListRow(order); row.PropertyChanged += OnRowChanged; Orders.Add(row);
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
        { if (args.PropertyName == nameof(PurchaseOrderListRow.IsChecked)) { OnPropertyChanged(nameof(CheckedCount)); RefreshCommands(); } }

        // 单张与批量共享门禁，冻结目录和订单身份；迟到的进度或结果不能回填新账号页面。
        private async Task ExportAsync(bool checkedOrders)
        {
            if (!CanBrowse || string.IsNullOrWhiteSpace(OutputPath)) return;
            var ids = checkedOrders ? Orders.Where(row => row.IsChecked).Select(row => row.Order.PurchaseOrderId).ToArray()
                : SelectedOrder != null && Orders.Contains(SelectedOrder) ? new[] { SelectedOrder.Order.PurchaseOrderId } : new Guid[0];
            if (ids.Length == 0) return;
            var path = OutputPath; var operation = new CancellationTokenSource(); var version = session.Capture().Version;
            export = operation; IsBusy = true; ErrorMessage = ""; ExportReport = "";
            OnPropertyChanged(nameof(CanBrowse)); OnPropertyChanged(nameof(IsExporting));
            try
            {
                var result = await exporter.ExportAsync(ids, path, message =>
                {
                    if (CanAcceptExport(operation, version)) StatusText = message;
                }, operation.Token);
                if (!CanAcceptExport(operation, version)) return;
                StatusText = "导出完成：成功 " + result.Files.Count + " 张，失败 " + result.FailedCount + " 张。";
                ExportReport = string.Join(Environment.NewLine, result.Files.Concat(result.Messages));
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested || session.Capture().Version != version) { }
            catch (Exception ex)
            {
                if (CanAcceptExport(operation, version))
                {
                    ErrorMessage = ex is ArgumentException ? ex.Message : ex is UnauthorizedAccessException
                        ? "没有输出文件夹的写入权限。" : ex is System.IO.IOException ? "无法创建输出文件夹，请检查路径和磁盘空间。" : errors.Describe(ex);
                    StatusText = "采购订单导出未完成。";
                }
            }
            finally
            {
                if (ReferenceEquals(export, operation))
                { export = null; IsBusy = false; OnPropertyChanged(nameof(CanBrowse)); OnPropertyChanged(nameof(IsExporting)); RefreshCommands(); }
                operation.Dispose();
            }
        }

        private bool CanAcceptExport(CancellationTokenSource operation, long version) => !IsDisposed
            && !operation.IsCancellationRequested && ReferenceEquals(export, operation) && session.Capture().Version == version;
        // 会话通知可能来自 HTTP 线程；立即取消，不能等 Excel 完成当前 COM 批次后才撤销。
        private void CancelExport()
        {
            try { export?.Cancel(); }
            catch (ObjectDisposedException) { /* 旧批次已结束，结束后的取消无副作用。 */ }
        }
        private void ClearOrders()
        {
            foreach (var row in Orders) row.PropertyChanged -= OnRowChanged;
            Orders.Clear(); SelectedOrder = null; OnPropertyChanged(nameof(CheckedCount));
        }
        // 事件可由 HTTP 线程触发；清空旧账号筛选、行及勾选后，等待用户首次显示或主动查询。
        private async void OnSessionChanged(object sender, EventArgs args)
        {
            CancelExport();
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (IsDisposed || sessionVersion == session.Capture().Version) return;
                    sessionVersion = session.Capture().Version; initializedVersion = null;
                    query?.Cancel(); query = null; export = null; IsBusy = false;
                    ClearOrders(); Keyword = ""; CustomerCode = ""; Confirmation = -1; QuickView = PurchaseDueViewEnum.All;
                    pageIndex = 1; totalCount = 0; ErrorMessage = ""; ExportReport = ""; OnPropertyChanged(nameof(IsExporting));
                    StatusText = "账号已变更，请重新查询采购订单。";
                    OnPropertyChanged(nameof(PageStatus)); OnPropertyChanged(nameof(CanBrowse)); RefreshCommands();
                });
            }
            catch (OperationCanceledException) { /* 宿主已退出，不再通知页面。 */ }
        }
        /// <summary>销毁 Scope 时终止查询、解绑会话及行事件；隐藏窗格不触发释放。</summary>
        protected override void DisposeCore()
        {
            session.Changed -= OnSessionChanged;
            CancelExport(); export = null;
            query?.Cancel(); query = null; ClearOrders();
        }
    }
}
