using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SalesOrderViews.Models;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

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
        #region 需要的枚举
        //====静态枚举====
        //订单状态
        //审批状态

        //===用户枚举====
        //币种
        //付款方式
        //贸易条款
        //颜色

        //====动态===
        //客户实体列表(用api实时返回吧)



        #endregion

        #region 字段 / 事件

        // 中文注释：保留订单 ID 与服务依赖，兼容现有调用入口。
        private readonly int _salesOrderId;
        private readonly ISalesOrderApi _salesOrderApi;
        private readonly IDialogService _msg;

        // 中文注释：由 View 订阅，用于在保存完成后关闭弹窗并返回结果。
        public event Action<bool?> CloseWindow;

        #endregion

        #region 构造函数

        public SalesOrderDetailViewModel() : this(0, null, null)
        {
        }

        public SalesOrderDetailViewModel(int salesOrderId, ISalesOrderApi salesOrderApi, IDialogService dialogService)
        {
            _salesOrderId = salesOrderId;
            _salesOrderApi = salesOrderApi;
            _msg = dialogService;

            // 初始化行集合模型
            OrderLines = new SalesOrderLineList();

            // 中文注释：初始化订单头，并立即与明细集合建立绑定关系，
            // 这样行集合汇总变化时可以自动回写到订单头。
            OrderHeader = new SalesOrderHeaderModel();

            OrderLines.BindHeader(OrderHeader);

            // 监听行集合变化（影响 CanSave）
            OrderLines.PropertyChanged += OrderLines_PropertyChanged;
            OrderLines.Items.CollectionChanged += (s, e) =>
            {
                RaisePropertyChanged(nameof(CanSave));
                RaisePropertyChanged(nameof(SelectedCount));
                RaisePropertyChanged(nameof(HasSelectedItems));
                RaisePropertyChanged(nameof(IsAllChecked));
                SaveCommand.RaiseCanExecuteChanged();
                DeleteCheckedCommand?.RaiseCanExecuteChanged();
                InvertCheckCommand?.RaiseCanExecuteChanged();
            };

            // 初始化命令
            AddLineCommand = new RelayCommand(AddLine);
            DeleteLineCommand = new RelayCommand(DeleteLine, () => SelectedLine != null);
            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave);
            CancelCommand = new RelayCommand(CancelEdit);

            // 中文注释：复选框列相关命令统一通过 VM 暴露给 DataGridTemplateColumn 使用。
            ItemCheckedChangedCommand = new RelayCommand(UpdateSelectionState);
            DeleteCheckedCommand = new RelayCommand(DeleteChecked, () => HasSelectedItems);
            InvertCheckCommand = new RelayCommand(InvertCheck, () => OrderLines.Items.Count > 0);

            OrderHeader.货币 = "USD";
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
        public int SelectedCount => OrderLines.SelectedCount;

        /// <summary>
        /// 中文注释：是否存在勾选项，可用于批量操作按钮启用状态。
        /// </summary>
        public bool HasSelectedItems => OrderLines.HasSelectedItems;

        private void OrderLines_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OrderLines.IsDirty) ||
                e.PropertyName == nameof(OrderLines.IsAllValid))
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand.RaiseCanExecuteChanged();
            }

            if (e.PropertyName == nameof(OrderLines.IsAllChecked) ||
                e.PropertyName == nameof(OrderLines.SelectedCount) ||
                e.PropertyName == nameof(OrderLines.HasSelectedItems))
            {
                RaisePropertyChanged(nameof(IsAllChecked));
                RaisePropertyChanged(nameof(SelectedCount));
                RaisePropertyChanged(nameof(HasSelectedItems));
                DeleteCheckedCommand?.RaiseCanExecuteChanged();
                InvertCheckCommand?.RaiseCanExecuteChanged();
            }
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
        public RelayCommand ItemCheckedChangedCommand { get; }
        public RelayCommand DeleteCheckedCommand { get; }
        public RelayCommand InvertCheckCommand { get; }

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
            if (SelectedLine == null)
                return;

            OrderLines.DeleteLine(SelectedLine);
            SelectedLine = null;
        }

        /// <summary>
        /// 中文注释：批量删除当前所有已勾选行。
        /// 实际删除规则沉到 OrderLines 集合模型中，VM 只负责命令组合与状态刷新。
        /// </summary>
        private void DeleteChecked()
        {
            OrderLines.DeleteChecked();
            SelectedLine = null;
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(HasSelectedItems));
        }

        /// <summary>
        /// 中文注释：行内复选框状态变化后，主动刷新一次集合的全选状态与选中统计。
        /// 这样可与自定义 DataGridTemplateColumn 的命令机制直接配合。
        /// </summary>
        private void UpdateSelectionState()
        {
            OrderLines.UpdateSelectionState();
        }

        /// <summary>
        /// 中文注释：反选当前所有可见订单行。
        /// 当前详情页如果后续需要批量删除、批量导出，可直接复用这个能力。
        /// </summary>
        private void InvertCheck()
        {
            OrderLines.InvertCheck();
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
            OrderLines.LoadFromDto(dto.Lines);

            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(HasSelectedItems));
            RaisePropertyChanged(nameof(CanSave));
            SaveCommand.RaiseCanExecuteChanged();
            DeleteCheckedCommand?.RaiseCanExecuteChanged();
            InvertCheckCommand?.RaiseCanExecuteChanged();
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

            // 中文注释：获取当前行变更集合，后续可直接用于组装 DTO 并调用 API。
            var changeSet = OrderLines.GetChangeSet();

            // TODO: 根据 _salesOrderId 判断新增/编辑。
            // TODO: 使用 _salesOrderApi 组装并提交订单头与订单行数据。
            _ = _salesOrderId;
            _ = _salesOrderApi;

            // 中文注释：当前阶段先提交本地状态，保证 View 与 ViewModel 的交互闭环。
            OrderHeader.CommitChanges();
            foreach (var line in changeSet.AddList)
                line.CommitChanges();
            foreach (var line in changeSet.UpdateList)
                line.CommitChanges();

            RaisePropertyChanged(nameof(CanSave));
            SaveCommand.RaiseCanExecuteChanged();
            DeleteCheckedCommand?.RaiseCanExecuteChanged();
            InvertCheckCommand?.RaiseCanExecuteChanged();

            // 中文注释：通知窗口以“保存成功”结果关闭，兼容现有 ShowDialog 流程。
            CloseWindow?.Invoke(true);
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
    }
}