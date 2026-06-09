using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.CustomerViews.ViewModels;
using SwiftExport.View.FW.CustomerViews.Views;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SalesOrderViews.Models;
using SwiftExport.View.FW.SalesOrderViews.Views;
using SwiftExport.View.FW.Services;
using SwiftExport.View.FW.SystemViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SwiftExport.View.FW.SalesOrderViews.ViewModels
{
    public class SalesOrderDetailViewModel : ViewModelBaseEx
    {
        /// <summary> 弹窗后的结果, 通常为新建或编辑的订单 Uid </summary>
        public Guid ResultData { get; set; }

        /// <summary> 关闭弹窗的委托 </summary>
        public event EventHandler<bool> RequestClose;

        public SalesOrderDetailViewModel(ISalesOrderApi salesOrderApi, IDialogService dialogService, ICustomerApi customerApi, ISysDicCurrencyApi sysDicCurrencyApi, IDictCacheService dictCacheService, ICustomerProductApi customerProductApi = null, bool isEmbeddedMode = false)
        {
            _salesOrderApi = salesOrderApi ?? IocProvider.GetService<ISalesOrderApi>();
            _customerProductApi = customerProductApi;
            _msg = dialogService;
            IsEmbeddedMode = isEmbeddedMode;
            // 在这里局部获取服务，不要污染 ViewModel 的构造函数
            _sysDicCurrencyApi = sysDicCurrencyApi;
            _dictCacheService = dictCacheService;


            // 中文注释：优先使用构造注入，缺省时回退到全局 IoC，确保详情页弹窗独立可用。
            _customerApi = customerApi;

            CurrencyList = new CurrencyList();
            // 初始化行集合模型
            OrderLines = new SalesOrderLineList();

            OrderHeader = new SalesOrderHeaderModel();
            OrderHeader.PropertyChanged += (s, e) =>
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand.RaiseCanExecuteChanged();
            };

            // 初始化命令
            AddLineCommand = new RelayCommand(AddLine);

            BatchGetSalesProductCommand = new RelayCommand(BatchGetSalesProduct);

            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave);
            RegisterBusyNotifyCommand(SaveCommand);
            OrderLines.RegisterDirtyNotifyCommand(SaveCommand);
            


            DeleteLineCommand = new RelayCommand(DeleteLine, () => (OrderLines.CheckedCount > 0 && IsNotBusy) || SelectedLine != null);
            OrderLines.RegisterCheckedNotifyCommand(DeleteLineCommand);
            RegisterBusyNotifyCommand(DeleteLineCommand);

            InvertCheckCommand = new RelayCommand(InvertCheck, () => OrderLines.Items.Count > 0);
            OrderLines.RegisterCheckedNotifyCommand(InvertCheckCommand);


            CancelCommand = new RelayCommand(CancelEdit);

            GetNewSoNoCommand = new RelayCommand(async () => await GetNewSoNo());


            // 中文注释：打开客户选择对话框，供单据中选择客户并自动带出客户默认信息。
            CostomerSelectorCommand = new RelayCommand(OpenCustomerSelectorDialog);

            SearchSuggestionsProductCommand = new RelayCommand<string>(async keyword => await SearchSuggestionsProductAsync(keyword));

            OrderHeader.Currency = CurrencyEnum.USD;

            _ = LoadFilterDataAsync();

        }

        #region 字段 / 事件

        // 中文注释：保留订单 Uid 与服务依赖，兼容现有调用入口。
        public Guid SalesOrderId { get; set; }
        private readonly ISalesOrderApi _salesOrderApi;
        private readonly ICustomerApi _customerApi;
        private readonly ICustomerProductApi _customerProductApi;
        private readonly IDialogService _msg;
        private readonly ISysDicCurrencyApi _sysDicCurrencyApi;
        private readonly IDictCacheService _dictCacheService;
        /// <summary>
        /// 是否嵌入模式。
        /// true：作为 UserControl 嵌入，不触发窗口关闭语义；
        /// false：独立弹窗模式，保留关闭事件。
        /// </summary>
        public bool IsEmbeddedMode { get; set; }

        // 中文注释：由 View 订阅，用于在保存完成后关闭弹窗并返回结果。
        public event Action<bool?> CloseWindow;

        #endregion

        #region 字典与下拉数据加载 (Dictionary & Lookups)
        //货币下拉框数据源
        public CurrencyList CurrencyList { get; }
        private ObservableCollection<string> _incotermList = new ObservableCollection<string>();
        public ObservableCollection<string> IncotermList
        {
            get => _incotermList;
            set => Set(ref _incotermList, value);
        }
        private ObservableCollection<string> _paymentMethodList = new ObservableCollection<string>();
        public ObservableCollection<string> PaymentMethodList
        {
            get => _paymentMethodList;
            set => Set(ref _paymentMethodList, value);
        }


        private async Task LoadFilterDataAsync()
        {
            try
            {
                if (_sysDicCurrencyApi != null)
                {
                    var currencyResult = await _sysDicCurrencyApi.GetEnabledListAsync();
                    if (currencyResult?.Data != null)
                    {
                        var data = currencyResult.Data as IEnumerable<SysDicCurrencyDto>;
                        // 确保回到 UI 线程更新集合，防止线程冲突导致图片消失
                        DispatcherProvider.Invoke(() =>
                        {
                            CurrencyList.ReLoadRangeFromDtoList(data.ToList());
                        });
                    }
                }

                if (_dictCacheService != null)
                {
                    var incdata = _dictCacheService.GetEnValues(DictCodes.Incoterm);
                    if (incdata != null)
                    {
                        DispatcherProvider.Invoke(() =>
                        {
                            IncotermList = new ObservableCollection<string>(incdata);
                        });
                    }

                    var paydata = _dictCacheService.GetEnValues(DictCodes.PaymentMethod);
                    if (paydata != null)
                    {
                        DispatcherProvider.Invoke(() =>
                        {
                            PaymentMethodList = new ObservableCollection<string>(paydata);
                        });
                    }
                }
            }
            catch
            {
                // 静默处理或打日志，不影响主流程
            }
        }


        #endregion


        #region 行集合模型

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private SalesOrderHeaderModel _orderHeader;
        public SalesOrderHeaderModel OrderHeader
        {
            get => _orderHeader;
            set => SetProperty(ref _orderHeader, value);
        }

        public SalesOrderLineList OrderLines { get; }

        private SalesOrderLineModel _selectedLine;

        /// <summary>当前选中行</summary>
        public SalesOrderLineModel SelectedLine
        {
            get => _selectedLine;
            set
            {
                if (SetProperty(ref _selectedLine, value))
                {
                    DeleteLineCommand.RaiseCanExecuteChanged();
                }
            }
        }

        #endregion
        // ============================================================
        // 保存按钮状态
        // ============================================================
        #region 保存按钮状态（CanSave）

        public bool CanSave =>
            OrderHeader != null &&
            OrderHeader.IsValid &&
            OrderLines.IsAllValid &&
            (OrderHeader.IsDirty || OrderLines.IsDirty);

        #endregion

        #region 命令

        public RelayCommand AddLineCommand { get; }
        public RelayCommand BatchGetSalesProductCommand { get; }
        public RelayCommand DeleteLineCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand CostomerSelectorCommand { get; }
        public RelayCommand InvertCheckCommand { get; }
        public RelayCommand GetNewSoNoCommand { get; }
        public RelayCommand MatchProductByCustomerSkuCommand { get; } = new RelayCommand(() => { /* 预留 SKU 联动命令实现 */ });
        public RelayCommand MatchProductBySkuCommand { get; } = new RelayCommand(() => { /* 预留 SKU 联动命令实现 */ });
        #endregion

        #region 行操作

        private void AddLine()
        {
            var line = new SalesOrderLineModel();
            line.MarkAsAdded();
            line.LineNumber = OrderLines.Items.Count + 1;
            OrderLines.Items.Add(line);
            SelectedLine = line;
        }

        private void DeleteLine()
        {
            var checkItems = OrderLines.GetCheckedItems();
            if (checkItems.Count == 0)
            {
                if (SelectedLine == null) return;

                if (SelectedLine.Uid == Guid.Empty)
                {
                    var item = SelectedLine;
                    SelectedLine = null;
                    var tempIndex = OrderLines.Items.IndexOf(item);
                    OrderLines.Remove(item);
                    try
                    {
                        SelectedLine = OrderLines.Items[tempIndex - 1];
                    }
                    catch
                    {
                        SelectedLine = OrderLines.Items.FirstOrDefault();
                    }
                    return;
                }

            }
            else
            {
                // 中文注释：批量删除勾选行，先快照再逐项删除，避免迭代中修改集合抛出异常。
                var toDelete = checkItems.ToList();
                foreach (var item in toDelete)
                {
                    OrderLines.Remove(item);
                }
                SelectedLine = OrderLines.Items.FirstOrDefault();
            }
        }

        private void BatchGetSalesProduct()
        {

            try
            {
                // 中文注释：创建销售产品选择器 VM，初始客户默认取当前订单客户。
                var selectorVm = new SalesProductSelectorViewModel(_salesOrderApi, _customerApi, null);

                if (OrderHeader != null && OrderHeader.CustomerId != Guid.Empty)
                {
                    selectorVm.SelectedCustomerId = OrderHeader.CustomerId;
                }
                var selectorWindow = new SalesProductSeletorView(selectorVm);

                // 中文注释：以对话框方式打开，确认后读取 ViewModel.ResultData。
                var dialogResult = selectorWindow.ShowDialog();
                if (dialogResult != true)
                {
                    return;
                }

                var selectedDtos = selectorVm.ResultData ?? new List<SalesProductDto>();
                if (selectedDtos.Count == 0)
                {
                    StatusMessage = "未选择任何产品";
                    return;
                }

                ApplySelectedProductsToOrderLines(selectedDtos);
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开产品选择器失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 将选择器返回的产品列表批量回填到订单行。
        /// </summary>
        private void ApplySelectedProductsToOrderLines(IList<SalesProductDto> selectedDtos)
        {
            if (selectedDtos == null || selectedDtos.Count == 0)
            {
                return;
            }

            // 中文注释：按 SKU + 客户SKU 去重，避免重复添加相同行。
            var existingKeys = new HashSet<string>(
                OrderLines.Items.Select(x => BuildLineUniqueKey(x.Sku, x.CustomerSku)),
                StringComparer.OrdinalIgnoreCase);

            int addedCount = 0;
            foreach (var dto in selectedDtos)
            {
                var key = BuildLineUniqueKey(dto.Sku, dto.CustomerSku);
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                var model = new SalesOrderLineModel();
                model.LoadFromDto(dto, true);

                // 中文注释：新加行强制标记为新增状态，确保保存时可提交。
                model.MarkAsAdded();
                model.SalesOrderId = Guid.Empty;
                model.OrderNo = OrderHeader.OrderNo;

                OrderLines.Items.Add(model);
                existingKeys.Add(key);
                addedCount++;
            }

            if (addedCount > 0)
            {
                SelectedLine = OrderLines.Items.LastOrDefault();
                StatusMessage = $"已添加 {addedCount} 条产品到订单明细";
            }
            else
            {
                StatusMessage = "所选产品均已存在于当前订单明细中";
            }
        }

        /// <summary>
        /// 构建订单行唯一键（SKU + 客户SKU）。
        /// </summary>
        private static string BuildLineUniqueKey(string sku, string customerSku)
        {
            var skuPart = (sku ?? string.Empty).Trim();
            var customerSkuPart = (customerSku ?? string.Empty).Trim();
            return $"{skuPart}||{customerSkuPart}";
        }

        /// <summary>
        /// 中文注释：反选当前所有可见订单行。
        /// 当前详情页如果后续需要批量删除、批量导出，可直接复用这个能力。
        /// </summary>
        private void InvertCheck() => OrderLines.InvertCheckAll();


        /// <summary>
        /// 中文注释：弹出客户选择对话框，用户确认后将客户基础信息回填到订单头。
        /// 该入口为后续“选择客户带出更多信息”预留了统一扩展点。
        /// </summary>
        private void OpenCustomerSelectorDialog()
        {
            if (_customerApi == null)
            {
                return;
            }

            // 中文注释：直接创建客户选择对话框 VM 与 Window，保持与产品选择器一致的交互风格。
            var vm = new CustomerSelectorDialogViewModel(_customerApi);
            var window = new CustomerSelectorDialogWindow(vm);
            var result = window.ShowDialog();

            if (result != true || window.SelectedCustomer == null)
            {
                return;
            }

            // 中文注释：用户确认后回填订单头字段。
            ApplySelectedCustomerToOrderHeader(window.SelectedCustomer);
        }

        /// <summary>
        /// 中文注释：将客户主数据回填到订单头。
        /// 当前先回填常用字段（客户标识、收货信息、贸易条款默认值等），
        /// 后续如需扩展更多“带出”字段，可在此方法集中追加。
        /// </summary>
        private void ApplySelectedCustomerToOrderHeader(CustomerDto customer)
        {
            if (customer == null || OrderHeader == null)
            {
                return;
            }

            OrderHeader.CustomerId = customer?.Uid ?? Guid.Empty;
            OrderHeader.CustomerCode = customer.CustomerCode;

            // 中文注释：客户基础联系信息映射到订单收货信息，减少手工重复录入。
            OrderHeader.ShipToName = customer.ContactPerson;
            OrderHeader.ShipToAddress = customer.Address;
            OrderHeader.ShipToPhone = customer.PhoneNumber;

            // 中文注释：贸易条款相关默认值回填。
            OrderHeader.IncotermSortRemark = customer.IncotermSortRemark;
            OrderHeader.Incoterm = customer.Incoterm;
            OrderHeader.Incotermlocation = customer.IncotermLocation;
            OrderHeader.DepositRate = customer.DepositRate;
            OrderHeader.LeadTimeDays = customer.LeadTimeDays;
            OrderHeader.PaymentMethod = customer.PaymentMethod;
            OrderHeader.EtdSortRemark = customer.EtdSortRemark;
            OrderHeader.ShippedBy = customer.ShippedBy;

            // 中文注释：当客户默认币种有值时覆盖订单币种；无值则保留当前订单设置。
            if (!string.IsNullOrWhiteSpace(customer.Currency))
            {
                OrderHeader.Currency = customer.Currency;
            }

            //调用Api查找对应客户的所有产品信息填充到缓存
            _ = LoadCustomerProductsToCacheAsync(customer?.Uid ?? Guid.Empty);

        }

        private async Task LoadCustomerProductsToCacheAsync(Guid id)
        {
            var result = await _salesOrderApi.GetSalesProductPagedListAsync(
                new SalesProductSearchDto
                {
                    CustomerId = id,
                    PageIndex = 1,
                    PageSize = 1000
                });

            if (result?.Success != true || result.Data?.Items == null) return;

            CacheProducts(result.Data.Items.Adapt<List<SalesOrderLineDto>>());
        }

        private async Task GetNewSoNo()
        {
            var rst = await _salesOrderApi.GetNextOrderNoAsync();
            if (rst == null || !rst.Success || rst.Data == null)
            {
                if (_msg != null)
                    await _msg.ShowErrorAsync("获取订单编号失败：" + (rst?.Message ?? "未知错误"));
                return;
            }
            DispatcherProvider.Invoke(() =>
            {
                OrderHeader.OrderNo = rst.Data.DocumentNo;
            });
        }

        #endregion

        // ============================================================
        // 加载订单（从 DTO）
        // ============================================================
        #region 加载订单

        public void LoadFromDto(SalesOrderDetailDto dto)
        {
            if (dto == null) return;

            if (dto.Header != null)
                OrderHeader.LoadFromDto(dto.Header);

            // 中文注释：明细集合统一交给集合模型自身加载，避免 ViewModel 重复维护明细装载逻辑。
            OrderLines.ReLoadRangeFromDtoList(dto.Lines);

            RaisePropertyChanged(nameof(CanSave));
            SaveCommand.RaiseCanExecuteChanged();

        }


        public void InitForCreate()
        {
            OrderHeader = new SalesOrderHeaderModel
            {
                OrderDate = DateTime.Now
            };
            OrderLines.Items.Clear();
        }

        public async Task InitForEditAsync(Guid headId)
        {
            if (headId == Guid.Empty) return;

            var hdRst = await _salesOrderApi.GetByIdAsync(headId);
            if (hdRst == null || !hdRst.Success || hdRst.Data == null) return;

            OrderHeader = new SalesOrderHeaderModel();
            OrderHeader.LoadFromDto(hdRst.Data);

            OrderLines.Clear();
            var rst = await _salesOrderApi.GetSalesOrderLinesAsync(headId);
            if (rst.Success && rst.Data != null)
            {
                OrderLines.ReLoadRangeFromDtoList(rst.Data.ToList());
                StatusMessage = $"编辑订单：{OrderHeader.OrderNo}";
            }
            else
            {
                StatusMessage = "加载订单明细失败：" + rst.Message;
            }
        }


        #endregion

        // ============================================================
        // 保存订单
        // ============================================================
        #region 保存订单

        private async Task SaveAsync()
        {
            using (BusyScope())
            {
                try
                {
                    // 中文注释：先做异步校验，避免无效数据继续提交。
                    var asyncError = await OrderLines.ValidateAllAsync();
                    if (!string.IsNullOrEmpty(asyncError))
                    {
                        if (_msg != null)
                            await _msg.ShowWarningAsync(asyncError);
                        return;
                    }
                    if (!OrderHeader.IsValid)
                    {
                        if (_msg != null)
                            await _msg.ShowWarningAsync("订单头信息有误，请检查必填字段和字段格式。");
                        return;
                    }

                    // 中文注释：获取当前行变更集合，后续可直接用于组装 DTO 并调用 API。
                    var changeSet = OrderLines.GetChangeSet();
                    if (!OrderHeader.IsDirty && !changeSet.HasChanges)
                    {
                        if (_msg != null)
                            await _msg.ShowWarningAsync("没有任何变更，无需保存。");
                        return;
                    }

                    var saveRequest = new SalesOrderSaveDetailRequest
                    {
                        Header = OrderHeader.Adapt<SalesOrderHeaderDto>(),
                        Items = OrderLines.Items.Adapt<List<SalesOrderLineDto>>(),
                        DeletedItemIds = changeSet.DeleteIdList
                    };

                    // 发送给 API
                    var result = await _salesOrderApi.SaveSalesOrderDetailAsync(saveRequest);

                    if (result.Success)
                    {
                        _msg?.ShowCardAsync("保存成功,订单已保存到服务器。正在刷新数据...", 2000);
                        ResultData = result.Data.Header.Uid;
                        // 中文注释：通知窗口以“保存成功”结果关闭，兼容现有 ShowDialog 流程。
                        CloseWindow?.Invoke(true);
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("保存失败: " + result.Message);
                    }
                }
                catch (Exception ex)
                {
                    await _msg.ShowErrorAsync("保存过程中发生异常: " + ex.Message);
                }
            }

        }
        #endregion

        // ============================================================
        // 撤销编辑
        // ============================================================
        private void CancelEdit()
        {
            // 中文注释：当前详情窗口为弹窗模式，取消时直接关闭并放弃本次编辑即可。
            CloseWindow?.Invoke(false);
        }

        #region Popup区域
        private SalesOrderLineDto _selectedSuggestionsProduct;
        public SalesOrderLineDto SelectedSuggestionsProduct
        {
            get => _selectedSuggestionsProduct;
            set
            {
                if (SetProperty(ref _selectedSuggestionsProduct, value) && value != null && SelectedLine != null)
                {
                    // 用户选择建议后回填行数据
                    ApplyProductLinkage(value);
                }
            }
        }
        /// <summary>
        /// 应用产品联动字段到当前客户产品行。
        /// </summary>
        private void ApplyProductLinkage(SalesOrderLineDto product)
        {
            if (SelectedLine == null) return;
            SelectedLine.LoadFromDto(product);
        }

        // ===================== SKU联动缓存 =====================
        // 中文注释：支持“SKU”和“客户SKU”两种入口的快速匹配缓存。
        private readonly Dictionary<string, SalesOrderLineDto> _skuProductCache = new Dictionary<string, SalesOrderLineDto>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SalesOrderLineDto> _customerSkuProductCache = new Dictionary<string, SalesOrderLineDto>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _cacheFetchTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Task> _inflightCacheFetchTasks = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheFetchDebounceWindow = TimeSpan.FromMilliseconds(30);

        public ObservableCollection<SalesOrderLineDto> ProductSuggestions { get; } = new ObservableCollection<SalesOrderLineDto>();
        public ICommand SearchSuggestionsProductCommand { get; }
        private CancellationTokenSource _cts;
        private async Task SearchSuggestionsProductAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                ProductSuggestions.Clear();
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                // 1. 先查本地缓存，即时响应
                var local = SearchFromCache(keyword).Take(20).ToList();
                if (local.Count > 0)
                    ResetCollection(ProductSuggestions, local);

                // 2. 后台补充 API，拉取后写入缓存
                await EnsureCacheByKeywordAsync(keyword);

                // CancellationToken 已取消则不刷新 UI
                if (token.IsCancellationRequested) return;

                var merged = SearchFromCache(keyword).Take(20).ToList();
                ResetCollection(ProductSuggestions, merged);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // 静默，不打断编辑流程
            }
        }

        /// <summary> 本地缓存搜索（SKU + 客户SKU 两个缓存都查）</summary>
        private IEnumerable<SalesOrderLineDto> SearchFromCache(string keyword)
        {
            var key = (keyword ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(key))
                return Enumerable.Empty<SalesOrderLineDto>();

            // 用 Sku 去重（同一产品在两个缓存中均存在时，以 Sku 作为唯一标识避免重复）
            // 注意：不能用 Id 去重，因为候选 DTO 的 Id 可能全为 0，导致只能取到一条
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<SalesOrderLineDto>();
            foreach (var kv in _customerSkuProductCache)
            {
                if (kv.Key.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                    && seen.Add(kv.Value.Sku ?? kv.Key))
                    results.Add(kv.Value);
            }

            foreach (var kv in _skuProductCache)
            {
                if (kv.Key.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                    && seen.Add(kv.Value.Sku ?? kv.Key))
                    results.Add(kv.Value);
            }

            return results;
        }
        /// <summary>用户输入时通过 API 拉取候选并写入缓存.</summary>
        private async Task EnsureCacheByKeywordAsync(string keyword)
        {
            if (_salesOrderApi == null || string.IsNullOrWhiteSpace(keyword)) return;

            var key = keyword.Trim();

            if (_cacheFetchTimes.TryGetValue(key, out var last)
                && DateTime.Now - last < CacheFetchDebounceWindow)
                return;

            if (_inflightCacheFetchTasks.TryGetValue(key, out var inflight))
            {
                await inflight;
                return;
            }

            var task = EnsureCacheByKeywordCoreAsync(key);
            _inflightCacheFetchTasks[key] = task;
            try { await task; }
            finally { _inflightCacheFetchTasks.Remove(key); }
        }

        /// <summary>实际执行缓存拉取。</summary>
        private async Task EnsureCacheByKeywordCoreAsync(string key)
        {
            _cacheFetchTimes[key] = DateTime.Now;

            var result = await _salesOrderApi.GetSalesProductPagedListAsync(
                new SalesProductSearchDto
                {
                    BatchKeywords = new List<string> { key },
                    CustomerId = OrderHeader?.CustomerId ?? Guid.Empty,
                    PageIndex = 1,
                    PageSize = 100
                });

            if (result?.Success != true || result.Data?.Items == null) return;

            CacheProducts(result.Data.Items.Adapt<IEnumerable<SalesOrderLineDto>>());
        }

        private void CacheProducts(IEnumerable<SalesOrderLineDto> items)
        {
            foreach (var item in items ?? Enumerable.Empty<SalesOrderLineDto>())
            {
                if (!string.IsNullOrWhiteSpace(item.Sku))
                    _skuProductCache[item.Sku.Trim()] = item;

                if (!string.IsNullOrWhiteSpace(item.CustomerSku))
                    _customerSkuProductCache[item.CustomerSku.Trim()] = item;
            }
        }

        /// <summary> 重新设置待选内容 </summary>
        private static void ResetCollection<T>(ObservableCollection<T> collection, IList<T> newItems)
        {
            DispatcherProvider.BeginInvoke(() =>
            {
                collection.Clear();
                foreach (var item in newItems)
                    collection.Add(item);
            });
        }



        #endregion







    }
}