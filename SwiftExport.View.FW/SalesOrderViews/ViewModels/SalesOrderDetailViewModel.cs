using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.CustomerViews.ViewModels;
using SwiftExport.View.FW.CustomerViews.Views;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SalesOrderViews.Controls;
using SwiftExport.View.FW.SalesOrderViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExport.View.FW.SalesOrderViews.ViewModels
{
    /// <summary>
    /// 销售订单详情编辑 ViewModel
    /// 负责：
    /// 1. 主表字段（订单头）
    /// 2. 行集合模型（SalesOrderLineList）
    /// 3. 保存按钮状态（CanSave）
    /// 4. API 调用（保存/加载）
    /// </summary>
    public class SalesOrderDetailViewModel : ViewModelBaseEx
    {
        // ===================== SKU联动缓存 =====================
        // 中文注释：支持“SKU”和“客户SKU”两种入口的快速匹配缓存。
        private readonly Dictionary<string, SalesOrderLineDto> _skuProductCache = new Dictionary<string, SalesOrderLineDto>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SalesOrderLineDto> _customerSkuProductCache = new Dictionary<string, SalesOrderLineDto>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<SalesOrderLineModel> _linkingRows = new HashSet<SalesOrderLineModel>();
        private readonly HashSet<SalesOrderLineModel> _suppressLineNotifyRows = new HashSet<SalesOrderLineModel>();
        private readonly Dictionary<string, DateTime> _cacheFetchTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Task> _inflightCacheFetchTasks = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheFetchDebounceWindow = TimeSpan.FromMilliseconds(600);

        /// <summary>
        /// 中文注释：SKU缓存键列表，供 DataGridDropdownAssist 下拉候选使用。
        /// </summary>
        private List<string> _skuCacheKeys = new List<string>();
        public List<string> SkuCacheKeys
        {
            get => _skuCacheKeys;
            private set => SetProperty(ref _skuCacheKeys, value);
        }

        /// <summary>
        /// 中文注释：客户SKU缓存键列表，供 DataGridDropdownAssist 下拉候选使用。
        /// </summary>
        private List<string> _customerSkuCacheKeys = new List<string>();
        public List<string> CustomerSkuCacheKeys
        {
            get => _customerSkuCacheKeys;
            private set => SetProperty(ref _customerSkuCacheKeys, value);
        }


        #region 需要的枚举
        //====静态枚举====
        //OrderStatus
        //ApprovalStatusEnums

        //===用户枚举====
        //币种
        //付款方式
        //Incoterm
        //ColorNameEn

        //====动态===
        //客户实体列表(用api实时返回吧)



        #endregion

        #region 字段 / 事件

        // 中文注释：保留订单 ID 与服务依赖，兼容现有调用入口。
        private readonly int _salesOrderId;
        private readonly ISalesOrderApi _salesOrderApi;
        private readonly ICustomerApi _customerApi;
        private readonly ICustomerProductApi _customerProductApi;
        private readonly IDialogService _msg;

        /// <summary>
        /// 是否嵌入模式。
        /// true：作为 UserControl 嵌入，不触发窗口关闭语义；
        /// false：独立弹窗模式，保留关闭事件。
        /// </summary>
        public bool IsEmbeddedMode { get; set; }

        // 中文注释：由 View 订阅，用于在保存完成后关闭弹窗并返回结果。
        public event Action<bool?> CloseWindow;

        #endregion

        #region 构造函数

        public SalesOrderDetailViewModel() : this(0, null, null, null, null)
        {
        }

        public SalesOrderDetailViewModel(int salesOrderId, ISalesOrderApi salesOrderApi, IDialogService dialogService, ICustomerApi customerApi = null, ICustomerProductApi customerProductApi = null, bool isEmbeddedMode = false)
        {
            _salesOrderId = salesOrderId;
            _salesOrderApi = salesOrderApi ?? IocProvider.Get<ISalesOrderApi>();
            _customerProductApi = customerProductApi;
            _msg = dialogService;
            IsEmbeddedMode = isEmbeddedMode;
            // 中文注释：优先使用构造注入，缺省时回退到全局 IoC，确保详情页弹窗独立可用。
            _customerApi = customerApi ?? IocProvider.Get<ICustomerApi>();

            // 初始化行集合模型
            OrderLines = new SalesOrderLineList();

            // 中文注释：初始化订单头，并立即与明细集合建立绑定关系，
            // 这样行集合汇总变化时可以自动回写到订单头。
            OrderHeader = new SalesOrderHeaderModel();
            OrderLines.BindHeader(OrderHeader);

            // 预留：单元格模式联动（当前按需求禁用）
            OrderLines.Items.CollectionChanged += OrderLineItems_CollectionChanged;
            AttachItemHandlers(OrderLines.Items);

            // 初始化命令
            AddLineCommand = new RelayCommand(AddLine);
            RegisterBusyNotifyCommand(AddLineCommand);

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

            // 中文注释：统一监听订单行列表状态变化，集中刷新 VM 代理属性。
            OrderLines.ListStateChanged += OnOrderLinesListStateChanged;

            // 中文注释：Popup 提交命令由自定义控件回传选中项。
            CommitPopupSelectionCommand = new RelayCommand<object>(CommitPopupSelection);
            OpenPopupCommand = new RelayCommand<object>(OpenPopupForRow);
            TriggerPopupButtonInCurrentCellCommand = new RelayCommand<object>(TriggerPopupButtonInCurrentCell);

            OrderHeader.Currency = "USD";
        }

        #endregion

        // ============================================================
        // 订单头（Header）
        // ============================================================
        #region 订单头,xaml绑定到集合下面的属性上.
        /// <summary>
        /// 订单头（Header）
        /// </summary>
        private SalesOrderHeaderModel _orderHeader;
        public SalesOrderHeaderModel OrderHeader
        {
            get => _orderHeader;
            set
            {
                if (_orderHeader == value) return;

                // 中文注释：切换 Header 时先解除旧对象事件，避免重复订阅。
                if (_orderHeader != null)
                    _orderHeader.PropertyChanged -= Header_PropertyChanged;

                if (SetProperty(ref _orderHeader, value))
                {
                    if (_orderHeader != null)
                        _orderHeader.PropertyChanged += Header_PropertyChanged;

                    // 中文注释：Header 替换后，明细集合需要重新绑定新的订单头实例。
                    OrderLines?.BindHeader(_orderHeader);

                    RaisePropertyChanged(nameof(CanSave));
                    SaveCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 中文注释：详情页状态提示信息。
        /// 当前主要用于弹出搜索前的前置校验提示（例如未选择客户）。
        /// </summary>
        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private void Header_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 中文注释：Header 任意字段变化都可能影响主表校验和脏状态，因此统一刷新 CanSave。
            RaisePropertyChanged(nameof(CanSave));
            SaveCommand.RaiseCanExecuteChanged();
        }

        #endregion

        // ============================================================
        // 从表（OrderLines）
        // ============================================================
        #region 行集合模型
        public SalesOrderLineList OrderLines { get; }

        private SalesOrderLineModel _selectedLine;
        /// <summary>
        /// 当前选中行
        /// </summary>
        public SalesOrderLineModel SelectedLine
        {
            get => _selectedLine;
            set
            {
                if (SetProperty(ref _selectedLine, value))
                    DeleteLineCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// 中文注释：表头全选复选框代理属性。
        /// XAML 中的 CheckBoxDataGridTemplateColumn 直接绑定 VM 的 IsAllChecked，
        /// 实际逻辑由 OrderLines 统一维护。
        /// </summary>
        public bool IsAllChecked
        {
            get => OrderLines.IsAllChecked;
            set
            {
                if (OrderLines.IsAllChecked == value)
                    return;

                OrderLines.IsAllChecked = value;
                RaisePropertyChanged(nameof(IsAllChecked));
                RaisePropertyChanged(nameof(SelectedCount));
                RaisePropertyChanged(nameof(HasSelectedItems));
            }
        }

        /// <summary>
        /// 中文注释：当前已勾选行数，主要用于工具栏或状态栏展示。
        /// </summary>
        public int SelectedCount => OrderLines.CheckedCount;

        /// <summary>
        /// 中文注释：是否存在勾选项，可用于批量操作按钮启用状态。
        /// </summary>
        public bool HasSelectedItems => OrderLines.CheckedCount > 0;

        /// <summary>
        /// 中文注释：订单行集合状态统一回调。
        /// 这里集中刷新 Save/选择相关代理属性，避免在多个事件里重复写同样逻辑。
        /// </summary>
        private void OnOrderLinesListStateChanged()
        {
            RaisePropertyChanged(nameof(CanSave));
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(HasSelectedItems));
            SaveCommand?.RaiseCanExecuteChanged();
            DeleteLineCommand?.RaiseCanExecuteChanged();
            InvertCheckCommand?.RaiseCanExecuteChanged();
        }

        #endregion
        // ============================================================
        // 保存按钮状态
        // ============================================================
        #region 保存按钮状态（CanSave）

        /// <summary>
        /// 保存按钮是否可用：
        /// 1. 主表字段有效
        /// 2. 行集合全部有效（IsAllValid）
        /// 3. 存在脏数据（主表或行）
        /// </summary>
        public bool CanSave =>
            OrderHeader != null &&
            OrderHeader.IsValid &&
            OrderLines.IsAllValid &&
            (OrderHeader.State != ModelState.Unchanged || OrderLines.IsDirty);

        #endregion

        // ============================================================
        // 命令
        // ============================================================
        #region 命令

        public RelayCommand AddLineCommand { get; }
        public RelayCommand DeleteLineCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand CostomerSelectorCommand { get; }
        public ICommand ItemCheckedChangedCommand { get; }//只是为了xaml里不触发绑定警告
        public RelayCommand InvertCheckCommand { get; }
        public RelayCommand GetNewSoNoCommand { get; }
        public RelayCommand<object> CommitPopupSelectionCommand { get; }
        public RelayCommand<object> OpenPopupCommand { get; }
        public RelayCommand<object> TriggerPopupButtonInCurrentCellCommand { get; }

        #endregion
        // ============================================================
        // 行操作
        // ============================================================
        #region 行操作

        private void AddLine()
        {
            var line = new SalesOrderLineModel();
            line.MarkAsAdded();

            OrderLines.Items.Add(line);
            SelectedLine = line;
        }

        private void DeleteLine()
        {
            var checkItems = OrderLines.GetCheckedItems();
            if (checkItems.Count == 0)
            {
                if (SelectedLine == null) return;
                if (SelectedLine.Id <= 0)
                {
                    var tempIndex = OrderLines.Items.IndexOf(SelectedLine);
                    OrderLines.Remove(SelectedLine);
                    try
                    {
                        SelectedLine = OrderLines.Items[tempIndex];

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
                foreach (var item in OrderLines.Items)
                {
                    OrderLines.Remove(item);
                }
                SelectedLine = OrderLines.Items.FirstOrDefault();
            }
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

            OrderHeader.CustomerId = customer.Id;
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
        }

        private async Task GetNewSoNo()
        {
            var rst = await _salesOrderApi.GetNextOrderNoAsync(new DocumentNoRequestDto() { DocType = DocumentTypeStaticEnums.SalesOrder });
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

        #endregion

        // ============================================================
        // 保存订单
        // ============================================================
        #region 保存订单

        private async Task SaveAsync()
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
            if (OrderHeader.State == ModelState.Unchanged && !changeSet.HasChanges)
            {
                if (_msg != null)
                    await _msg.ShowWarningAsync("没有任何变更，无需保存。");
                return;
            }

            // 中文注释：将需要删除的行 ID 添加到 DTO 的 DeletedLineIds 集合中。
            var saveDto = new SalesOrderDetailDto
            {
                Header = OrderHeader.Adapt<SalesOrderHeaderDto>(),
                Lines= OrderLines.Items.Select(line => line.Adapt<SalesOrderLineDto>()).ToList(),
                DeletedLineIds = changeSet.DeleteIdList
            };

            // 发送给 API
            var result = await _salesOrderApi.SaveSalesOrderDetailAsync(saveDto);

            if (result.Success)
            {
                _msg?.ShowCardAsync("保存成功,订单已保存到服务器。正在刷新数据...", 2000);
                var rst= await _salesOrderApi.GetSalesOrderDetailAsync(OrderHeader.Id);
                if (rst.Success)
                {
                    this.LoadFromDto(rst.Data);
                }

                // 中文注释：通知窗口以“保存成功”结果关闭，兼容现有 ShowDialog 流程。
                CloseWindow?.Invoke(true);
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
        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set => SetProperty(ref _isPopupOpen, value);
        }
        private bool _isPopupOpen;

        private UIElement _popupPlacementTarget;
        /// <summary>
        /// Popup 锚点控件（通常是单元格中的“搜索按钮”）。
        /// </summary>
        public UIElement PopupPlacementTarget
        {
            get => _popupPlacementTarget;
            set => SetProperty(ref _popupPlacementTarget, value);
        }

        /// <summary>
        /// 打开行级 Popup（由单元格按钮触发）。
        /// </summary>
        private void OpenPopupForRow(object rowObj)
        {
            var anchor = rowObj as FrameworkElement;
            if (anchor is TextBox)
            {
                // 中文注释：编辑态 TextBox 可能在失焦后被 DataGrid 回收，
                // 这里提升锚点到稳定的 DataGridCell，避免 Popup 跑到屏幕左上角。
                anchor = FindAncestor<DataGridCell>(anchor);
            }

            var row = anchor?.DataContext as SalesOrderLineModel
                      ?? rowObj as SalesOrderLineModel
                      ?? SelectedLine;
            if (row == null) return;

            SelectedLine = row;
            PopupPlacementTarget = anchor;
            IsPopupOpen = true;
        }

        /// <summary>
        /// 在当前编辑单元格中触发右侧“搜索按钮”的点击逻辑。
        /// 用于 DataGridTextColumn 右侧按钮入口（不新增独立列）。
        /// </summary>
        private void TriggerPopupButtonInCurrentCell(object senderObj)
        {
            var textBox = senderObj as TextBox;
            if (textBox == null)
                return;

            var anchorCell = FindAncestor<DataGridCell>(textBox);
            OpenPopupForRow(anchorCell ?? (object)textBox);
        }

        /// <summary>
        /// 可视树向上查找父级元素。
        /// </summary>
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T target)
                    return target;

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        /// <summary>
        /// 接收 Popup 控件的提交结果并回填当前订单行。
        /// </summary>
        private void CommitPopupSelection(object submitObj)
        {
            var args = submitObj as SalesOrderLinePopupSelectorSubmitArgs;
            var selectedPopupProduct = args?.SelectedItem;
            if (args == null || selectedPopupProduct == null)
                return;

            using (BusyScope())
            {
                DispatcherProvider.BeginInvoke(() =>
                {
                    try
                    {
                        // 中文注释：优先使用控件回传的行对象；
                        // 单元格模式下 SelectedLine 可能为 null，因此再兜底当前单元格行。
                        var row = args.RowContext as SalesOrderLineModel
                                  ?? SelectedLine
                                  ?? GetCurrentCellRow();
                        if (row == null) return;
                        row.CustomerSKU = selectedPopupProduct.CustomerSKU;
                        //row.LoadFromDto(selectedPopupProduct);
                        // 中文注释：将联查结果字段回填到订单行模型，保持与 SalesOrderLineModel 字段语义一致。
                        //row.Sku = selectedPopupProduct.Sku;
                        //row.CustomerSKU = selectedPopupProduct.CustomerSKU;
                        //row.DescriptionEn = selectedPopupProduct.DescriptionEn;
                        //row.ColorNameEn = selectedPopupProduct.ColorNameEn;
                        //row.FeaturesEn = selectedPopupProduct.FeaturesEn;
                        //row.USDPrice = selectedPopupProduct.USDPrice ?? 0m;
                        //row.CNYPrice = selectedPopupProduct.CNYPrice ?? 0m;
                        //row.MainImage = selectedPopupProduct.MainImage;

                        // 中文注释：新行首次选品时，若数量未填写则默认置为 1，便于快速录单。
                        if (row.QTY <= 0)
                        {
                            row.QTY = 1;
                        }

                        SelectedLine = row;
                        IsPopupOpen = false;
                    }
                    finally
                    {

                    }
                });
            }

        }

        /// <summary>
        /// 在单元格模式下，从主窗口焦点链路兜底获取当前 DataGrid 行对象。
        /// 说明：Ctrl+D/粘贴等 Excel 快捷操作不依赖 SelectedItem，这里用于 Popup 提交兜底。
        /// </summary>
        private SalesOrderLineModel GetCurrentCellRow()
        {
            try
            {
                var focused = Keyboard.FocusedElement as DependencyObject;
                while (focused != null)
                {
                    var cell = focused as DataGridCell;
                    if (cell != null)
                    {
                        var row = DataGridRow.GetRowContainingElement(cell);
                        return row?.Item as SalesOrderLineModel;
                    }

                    focused = System.Windows.Media.VisualTreeHelper.GetParent(focused);
                }
            }
            catch
            {
                // 中文注释：兜底失败不抛异常，交由上层 null 判断。
            }

            return null;
        }

        #region 预留联动逻辑（当前按需求禁用，仅保留未来可用性）


        /// <summary>
        /// 监听列表新增/移除，维护每行 PropertyChanged 订阅。
        /// </summary>
        private void OrderLineItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e?.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<SalesOrderLineModel>())
                {
                    item.PropertyChanged -= SalesOrderLineItem_PropertyChanged;
                    item.PropertyChanged += SalesOrderLineItem_PropertyChanged;
                }
            }

            if (e?.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<SalesOrderLineModel>())
                {
                    item.PropertyChanged -= SalesOrderLineItem_PropertyChanged;
                }
            }
        }

        /// <summary>
        /// 初始化时为已有行绑定事件。
        /// </summary>
        private void AttachItemHandlers(IEnumerable<SalesOrderLineModel> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                item.PropertyChanged -= SalesOrderLineItem_PropertyChanged;
                item.PropertyChanged += SalesOrderLineItem_PropertyChanged;
            }
        }

        /// <summary>
        /// 当 CustomerSKU 或 Sku 结束编辑后（LostFocus 提交）触发联动。
        /// </summary>
        private async void SalesOrderLineItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null) return;
            if (e.PropertyName != nameof(SalesOrderLineModel.CustomerSKU)
                && e.PropertyName != nameof(SalesOrderLineModel.Sku))
                return;

            var row = sender as SalesOrderLineModel;// 当前行对象，可能为 null（例如单元格模式下 SelectedItem 可能为 null），后续联动逻辑需先行 null 判断。
            if (row == null) return;

            // 中文注释：程序回填阶段不再触发二次联动，避免重复通知与重复 API 请求。
            if (_suppressLineNotifyRows.Contains(row))
                return;

            await TryApplyLinkageAsync(row, e.PropertyName);
        }

        /// <summary>
        /// 根据行内 Sku 尝试联动产品信息。
        /// </summary>
        private async Task TryApplyLinkageAsync(SalesOrderLineModel row, string changedProperty)
        {
            if (row == null) return;
            if (_linkingRows.Contains(row)) return;

            var inputSku = (row.Sku ?? string.Empty).Trim();
            var inputCustomerSku = (row.CustomerSKU ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(inputSku) && string.IsNullOrWhiteSpace(inputCustomerSku))
            {
                // 中文注释：当 Sku 被清空时，同步清空联动字段，避免旧值残留。
                DispatcherProvider.Invoke(() =>
                {
                    row.Id = 0;
                    row.CustomerSKU = string.Empty;
                    row.DescriptionEn = string.Empty;
                    row.MaterialType = string.Empty;
                    row.MainImage = string.Empty;
                });
                return;
            }

            _linkingRows.Add(row);
            try
            {
                SalesOrderLineDto product = null;
                var lookupKey = changedProperty == nameof(SalesOrderLineModel.Sku) ? inputSku : inputCustomerSku;

                // 中文注释：优先按“触发字段”命中缓存，避免误匹配。
                if (changedProperty == nameof(SalesOrderLineModel.Sku) && !string.IsNullOrWhiteSpace(inputSku))
                {
                    _skuProductCache.TryGetValue(inputSku, out product);
                }
                else if (changedProperty == nameof(SalesOrderLineModel.CustomerSKU) && !string.IsNullOrWhiteSpace(inputCustomerSku))
                {
                    _customerSkuProductCache.TryGetValue(inputCustomerSku, out product);
                }

                if (product == null)
                {
                    // 中文注释：缓存未命中时，按用户输入关键词走一次 API 拉取并更新缓存。
                    await EnsureCacheByKeywordAsync(lookupKey);
                    product = TryResolveProductFromCache(lookupKey);
                }

                if (product == null)
                {
                    DispatcherProvider.Invoke(() =>
                    {
                        ClearLinkedFieldsWhenNoMatch(row, changedProperty);
                    });
                    return;
                }

                DispatcherProvider.Invoke(() =>
                {
                    ApplyProductLinkage(row, product);
                });
            }
            catch
            {
                // 中文注释：联动失败不打断编辑流程，保持静默。
            }
            finally
            {
                _linkingRows.Remove(row);
            }
        }

        /// <summary>
        /// 根据关键字从本地缓存中严格匹配（忽略大小写、必须全匹配）。
        /// </summary>
        private SalesOrderLineDto TryResolveProductFromCache(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return null;

            var key = keyword.Trim();
            if (_skuProductCache.TryGetValue(key, out var bySku))
                return bySku;

            if (_customerSkuProductCache.TryGetValue(key, out var byCustomerSku))
                return byCustomerSku;

            return null;
        }

        /// <summary>
        /// 用户输入时通过 API 拉取候选并写入缓存。
        /// </summary>
        private async Task EnsureCacheByKeywordAsync(string keyword)
        {
            if (_salesOrderApi == null || string.IsNullOrWhiteSpace(keyword)) return;

            var normalizedKeyword = keyword.Trim();
            if (normalizedKeyword.Length == 0) return;

            // 中文注释：请求级防抖。相同关键字在短窗口内不重复请求。
            DateTime lastFetchAt;
            if (_cacheFetchTimes.TryGetValue(normalizedKeyword, out lastFetchAt))
            {
                if (DateTime.Now - lastFetchAt < CacheFetchDebounceWindow)
                    return;
            }

            // 中文注释：同一关键字请求去重。若已有进行中的请求则复用该任务。
            if (_inflightCacheFetchTasks.TryGetValue(normalizedKeyword, out Task inflightTask))
            {
                await inflightTask;
                return;
            }

            var fetchTask = EnsureCacheByKeywordCoreAsync(normalizedKeyword);
            _inflightCacheFetchTasks[normalizedKeyword] = fetchTask;

            try
            {
                await fetchTask;
            }
            finally
            {
                _inflightCacheFetchTasks.Remove(normalizedKeyword);
            }
        }

        /// <summary>
        /// 实际执行缓存拉取。
        /// </summary>
        private async Task EnsureCacheByKeywordCoreAsync(string normalizedKeyword)
        {
            _cacheFetchTimes[normalizedKeyword] = DateTime.Now;

            var result = await _salesOrderApi.GetSalesOrderLineSourcePagedListAsync(new SalesOrderLineSourceSearchDto
            {
                Keyword = normalizedKeyword,
                CustomerId = OrderHeader?.CustomerId ?? 0,
                PageIndex = 1,
                PageSize = 100
            });

            if (!result.Success || result.Data == null || result.Data.Items == null || result.Data.Items.Count == 0)
                return;

            CacheCustomerProducts(result.Data.Items);
        }

        /// <summary>
        /// 写入 SKU / 客户SKU 双缓存。
        /// </summary>
        private void CacheCustomerProducts(IEnumerable<SalesOrderLineDto> items)
        {
            foreach (var item in items ?? Enumerable.Empty<SalesOrderLineDto>())
            {
                if (!string.IsNullOrWhiteSpace(item.Sku))
                    _skuProductCache[item.Sku.Trim()] = item;

                if (!string.IsNullOrWhiteSpace(item.CustomerSKU))
                    _customerSkuProductCache[item.CustomerSKU.Trim()] = item;
            }

            // 中文注释：缓存更新后同步刷新用于下拉联想的键集合。
            SkuCacheKeys = _skuProductCache.Keys.OrderBy(x => x).ToList();
            CustomerSkuCacheKeys = _customerSkuProductCache.Keys.OrderBy(x => x).ToList();
        }

        /// <summary>
        /// 应用产品联动字段到当前客户产品行。
        /// </summary>
        private void ApplyProductLinkage(SalesOrderLineModel row, SalesOrderLineDto product)
        {
            if (row == null || product == null) return;

            _suppressLineNotifyRows.Add(row);
            try
            {
                row.LoadFromDto(product);
                //// 中文注释：程序回填阶段统一抑制行属性通知引发的二次联动。
                //row.Sku = product.Sku;
                //row.CustomerSKU = product.CustomerSKU;
                //row.DescriptionEn = product.DescriptionEn;
                //row.ColorNameEn = product.ColorNameEn;
                //row.MaterialType = product.DescriptionEn;
                //row.FeaturesEn = product.FeaturesEn;
                //row.USDPrice = product.USDPrice ?? row.USDPrice;
                //row.CNYPrice = product.CNYPrice ?? row.CNYPrice;
                //row.MainImage = product.MainImage;
                if (row.QTY <= 0)
                {
                    row.QTY = 1;
                }
            }
            finally
            {
                _suppressLineNotifyRows.Remove(row);
            }
        }

        /// <summary>
        /// 用户输入 Sku/CustomerSKU 未匹配时，清空该行联动字段，避免残留旧数据。
        /// </summary>
        private void ClearLinkedFieldsWhenNoMatch(SalesOrderLineModel row, string changedProperty)
        {
            if (row == null) return;

            _suppressLineNotifyRows.Add(row);
            try
            {
                // 中文注释：保留用户刚编辑的字段值，清空其余联动字段。
                if (changedProperty == nameof(SalesOrderLineModel.Sku))
                {
                    row.CustomerSKU = string.Empty;
                }
                else if (changedProperty == nameof(SalesOrderLineModel.CustomerSKU))
                {
                    row.Sku = string.Empty;
                }
                row.DescriptionEn = string.Empty;
                row.ColorNameEn = string.Empty;
                row.FeaturesEn = string.Empty;
                row.USDPrice = 0m;
                row.CNYPrice = 0m;
                row.PcsOfCarton = 0m;
                row.NwOfCarton = 0m;
                row.GwOfCarton = 0m;
                row.CbmOfCarton = 0m;
                row.MainImage = string.Empty;
            }
            finally
            {
                _suppressLineNotifyRows.Remove(row);
            }
        }


        #endregion

        #endregion







    }
}