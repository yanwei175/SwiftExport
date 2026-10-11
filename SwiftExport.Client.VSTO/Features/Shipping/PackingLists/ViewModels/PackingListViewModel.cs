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
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;

namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels
{
    /// <summary>装箱单只读侧栏，管理搜索、分页与当前页勾选；不访问 Excel COM 或执行导出。</summary>
    public sealed class PackingListViewModel : ViewModelBaseEx
    {
        private const int PageSize = 30;
        private readonly IPackingListApi api;
        private readonly SessionService session;
        private readonly CurrentUserService user;
        private readonly IUiDispatcher dispatcher;
        private readonly ApiCallRunner errors;
        private CancellationTokenSource query;
        private long sessionVersion;
        private long? initializedVersion;
        private bool customersReady;
        private string keyword = "", cacheMessage = "", statusText = "点击查询加载装箱单。";
        private string outputPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private int postingStatus = -1, pageIndex = 1, totalCount;
        private CustomerDto selectedCustomer;
        private PackingListRow selectedDocument;

        /// <summary>借用账号基础资料缓存，不编辑客户 DTO 或在页面关闭时清空共享缓存。</summary>
        public IReferenceDataCacheService ReferenceData { get; }
        /// <summary>搜索关键词，提交时修剪首尾空白。</summary>
        public string Keyword { get => keyword; set => SetProperty(ref keyword, value); }
        /// <summary>仅由客户选择控件提交身份，清空表示查询全部客户。</summary>
        public CustomerDto SelectedCustomer { get => selectedCustomer; set => SetProperty(ref selectedCustomer, value); }
        /// <summary>过账筛选，-1 表示全部，其他值对应共享枚举。</summary>
        public int PostingStatus { get => postingStatus; set => SetProperty(ref postingStatus, value); }
        /// <summary>预留输出路径，默认桌面；当前不检查目录或创建文件。</summary>
        public string OutputPath { get => outputPath; set => SetProperty(ref outputPath, value); }
        /// <summary>表格当前行，与批量勾选相互独立。</summary>
        public PackingListRow SelectedDocument { get => selectedDocument; set => SetProperty(ref selectedDocument, value); }
        /// <summary>当前已接纳查询的范围和账号变更提示。</summary>
        public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
        /// <summary>客户缓存失败单独提示，不覆盖列表查询错误；刷新会重新尝试加载。</summary>
        public string CacheMessage { get => cacheMessage; private set => SetProperty(ref cacheMessage, value); }
        /// <summary>当前页摘要与本地勾选，不保存跨页选择。</summary>
        public ObservableCollection<PackingListRow> Documents { get; } = new ObservableCollection<PackingListRow>();
        /// <summary>仅统计本页明确勾选的记录。</summary>
        public int CheckedCount => Documents.Count(row => row.IsChecked);
        /// <summary>页码按已接纳的总数计算，空结果显示第一页。</summary>
        public string PageStatus => "第 " + pageIndex + " / " + Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize)) + " 页";
        /// <summary>已验证账号的查看权限及页面忙碌状态；服务端仍负责最终授权。</summary>
        public bool CanBrowse => !IsDisposed && !IsBusy && user.HasPermission(PermissionKeys.ShippingPackingListView);
        /// <summary>直接对应现有过账状态，不增加生命周期或日期筛选。</summary>
        public IReadOnlyList<KeyValuePair<int, string>> PostingStatusOptions { get; } = new[]
        {
            new KeyValuePair<int, string>(-1, "全部"), new KeyValuePair<int, string>(0, "未过账"),
            new KeyValuePair<int, string>(1, "已过账"), new KeyValuePair<int, string>(8, "已冲销")
        };
        /// <summary>按当前筛选查询第一页。</summary>
        public IAsyncRelayCommand SearchCommand { get; }
        /// <summary>刷新当前页，客户缓存失败时同时重试基础资料。</summary>
        public IAsyncRelayCommand RefreshCommand { get; }
        /// <summary>查询上一页。</summary>
        public IAsyncRelayCommand PreviousPageCommand { get; }
        /// <summary>查询下一页。</summary>
        public IAsyncRelayCommand NextPageCommand { get; }
        /// <summary>接纳缓存选择器明确提交的客户，不把输入文字当作客户 ID。</summary>
        public IRelayCommand CustomerSelectedCommand { get; }
        /// <summary>清除客户筛选，下一次查询覆盖全部客户。</summary>
        public IRelayCommand CustomerClearedCommand { get; }

        /// <summary>仅登记依赖和命令，首次显示才加载客户及列表。</summary>
        public PackingListViewModel(IPackingListApi api, SessionService session, CurrentUserService user,
            IUiDispatcher dispatcher, IReferenceDataCacheService referenceData, ApiCallRunner errors)
        {
            this.api = api; this.session = session; this.user = user; this.dispatcher = dispatcher; this.errors = errors;
            ReferenceData = referenceData; Title = "装箱单"; sessionVersion = session.Capture().Version;
            SearchCommand = new AsyncRelayCommand(() => LoadAsync(1), () => CanBrowse);
            RefreshCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex), () => CanBrowse);
            PreviousPageCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex - 1), () => CanBrowse && pageIndex > 1);
            NextPageCommand = new AsyncRelayCommand(() => LoadAsync(pageIndex + 1), () => CanBrowse && (long)pageIndex * PageSize < totalCount);
            CustomerSelectedCommand = new RelayCommand<CustomerDto>(customer => SelectedCustomer = customer,
                customer => CanBrowse && customer != null && ReferenceData.Customers.Contains(customer));
            CustomerClearedCommand = new RelayCommand(() => SelectedCustomer = null, () => CanBrowse);
            foreach (var command in new IRelayCommand[] { SearchCommand, RefreshCommand, PreviousPageCommand, NextPageCommand,
                CustomerSelectedCommand, CustomerClearedCommand }) RegisterCommand(command);
            session.Changed += OnSessionChanged;
        }

        /// <summary>同一会话重复显示保留筛选和勾选；失败可通过查询或刷新重试。</summary>
        public async Task InitializeAsync()
        {
            if (!CanBrowse || initializedVersion == sessionVersion) return;
            initializedVersion = sessionVersion;
            await LoadAsync(1);
        }

        // 冻结筛选和请求身份；缓存失败仍查询全部客户，旧账号或已释放页面的结果不得接纳。
        private async Task LoadAsync(int targetPage)
        {
            if (!CanBrowse) return;
            var request = new PackingListManagerSearchRequest
            {
                Keyword = (Keyword ?? "").Trim(), CustomerId = SelectedCustomer?.Uid,
                PostingStatus = PostingStatus < 0 ? (PackingListPostingStatusEnum?)null : (PackingListPostingStatusEnum)PostingStatus,
                PageIndex = Math.Max(1, targetPage), PageSize = PageSize
            };
            var operation = new CancellationTokenSource(); var version = session.Capture().Version;
            query = operation; IsBusy = true; OnPropertyChanged(nameof(CanBrowse)); ErrorMessage = "";
            try
            {
                if (!customersReady)
                {
                    try
                    {
                        var cached = await ReferenceData.EnsureLoadedAsync();
                        if (!CanAccept(operation, version)) return;
                        customersReady = cached.Success;
                        CacheMessage = cached.Success ? "" : cached.Message + " 可查询全部客户，点击刷新重试客户资料。";
                    }
                    catch (Exception ex)
                    {
                        if (!CanAccept(operation, version)) return;
                        CacheMessage = errors.Describe(ex) + " 可查询全部客户，点击刷新重试客户资料。";
                    }
                }
                if (!CanAccept(operation, version)) return;
                var result = await api.SearchAsync(request, operation.Token);
                if (!CanAccept(operation, version)) return;
                if (result == null || !result.Success || result.Data == null)
                { ErrorMessage = result?.Error?.Message ?? "查询装箱单失败，请重试。"; return; }
                ClearDocuments();
                foreach (var document in result.Data.Items)
                {
                    var row = new PackingListRow(document); row.PropertyChanged += OnRowChanged; Documents.Add(row);
                }
                totalCount = result.Data.TotalCount; pageIndex = request.PageIndex;
                StatusText = "共 " + totalCount + " 张，本页 " + Documents.Count + " 张；勾选仅限本页。";
                OnPropertyChanged(nameof(PageStatus)); OnPropertyChanged(nameof(CheckedCount));
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            catch (Exception ex) { if (CanAccept(operation, version)) ErrorMessage = errors.Describe(ex); }
            finally
            {
                if (ReferenceEquals(query, operation))
                { query = null; IsBusy = false; OnPropertyChanged(nameof(CanBrowse)); RefreshCommands(); }
                operation.Dispose();
            }
        }

        // 请求和账号身份都必须仍有效，单靠取消传输无法阻止忽略取消的迟到响应。
        private bool CanAccept(CancellationTokenSource operation, long version) => !IsDisposed
            && !operation.IsCancellationRequested && ReferenceEquals(query, operation) && session.Capture().Version == version;
        private void OnRowChanged(object sender, PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(PackingListRow.IsChecked)) OnPropertyChanged(nameof(CheckedCount)); }
        // 接纳新页和销毁时对称释放行订阅，不把勾选状态留在 DTO。
        private void ClearDocuments()
        {
            foreach (var row in Documents) row.PropertyChanged -= OnRowChanged;
            Documents.Clear(); SelectedDocument = null; OnPropertyChanged(nameof(CheckedCount));
        }
        // 会话事件可能来自 HTTP 线程；回 UI 清除旧身份的数据和筛选，并允许新会话重新初始化。
        private async void OnSessionChanged(object sender, EventArgs args)
        {
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (IsDisposed || sessionVersion == session.Capture().Version) return;
                    sessionVersion = session.Capture().Version; initializedVersion = null; customersReady = false;
                    query?.Cancel(); query = null; IsBusy = false;
                    ClearDocuments(); Keyword = ""; SelectedCustomer = null; PostingStatus = -1;
                    pageIndex = 1; totalCount = 0; ErrorMessage = ""; CacheMessage = "";
                    StatusText = "账号已变更，请重新查询装箱单。";
                    OnPropertyChanged(nameof(PageStatus)); OnPropertyChanged(nameof(CanBrowse)); RefreshCommands();
                });
            }
            catch (OperationCanceledException) { /* 宿主已退出，不再回填页面。 */ }
        }
        /// <summary>窗格 Scope 销毁时取消查询并释放订阅；隐藏窗格不触发销毁。</summary>
        protected override void DisposeCore()
        {
            session.Changed -= OnSessionChanged;
            query?.Cancel(); query = null; ClearDocuments();
        }
    }
}
