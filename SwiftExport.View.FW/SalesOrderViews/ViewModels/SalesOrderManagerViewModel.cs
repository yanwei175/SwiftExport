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
    /// 销售订单管理 ViewModel（主从表可编辑模式）
    /// 负责：
    /// 1. 订单头列表（SalesOrderHeaderList）
    /// 2. 选中的订单头（SelectedOrderHeader）
    /// 3. 当前选中订单的行集合（CurrentOrderLines）
    /// 4. 主从表同时编辑、脏数据追踪、保存
    /// </summary>
    public class SalesOrderManagerViewModel : ViewModelBaseEx
    {
        #region 字段 / 事件

        private readonly ISalesOrderApi _salesOrderApi;
        private readonly IDialogService _msg;

        // 中文注释：由 View 订阅，用于在保存完成后关闭弹窗并返回结果。
        public event Action<bool?> CloseWindow;

        #endregion

        #region 构造函数

        public SalesOrderManagerViewModel() : this(null, null)
        {
        }

        public SalesOrderManagerViewModel(ISalesOrderApi salesOrderApi, IDialogService dialogService)
        {
            _salesOrderApi = salesOrderApi;
            _msg = dialogService;

            // 中文注释：初始化订单头列表（承载大量订单头、脏状态追踪、批量操作）。
            OrderHeaders = new SalesOrderHeaderList();

            // 中文注释：初始化当前选中订单的行集合（动态、可绑定到 SelectedOrderHeader）。
            CurrentOrderLines = new SalesOrderLineList();

            // 监听订单头列表变化（影响主表的 CanSave）
            OrderHeaders.PropertyChanged += OrderHeaders_PropertyChanged;

            // 监听当前行集合变化（影响从表的 CanSave、汇总同步）
            CurrentOrderLines.PropertyChanged += CurrentOrderLines_PropertyChanged;
            CurrentOrderLines.Items.CollectionChanged += (s, e) =>
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand.RaiseCanExecuteChanged();
            };

            // 初始化命令
            AddLineCommand = new RelayCommand(AddLine);
            DeleteLineCommand = new RelayCommand(DeleteLine, () => SelectedLine != null);
            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave);
        }

        #endregion

        // ============================================================
        // 主表：订单头列表
        // ============================================================
        #region 订单头列表

        public SalesOrderHeaderList OrderHeaders { get; }

        private SalesOrderHeaderModel _selectedOrderHeader;
        /// <summary>
        /// 当前选中的订单头。
        /// 当选中发生变化时，自动加载对应订单的行集合。
        /// </summary>
        public SalesOrderHeaderModel SelectedOrderHeader
        {
            get => _selectedOrderHeader;
            set
            {
                if (_selectedOrderHeader == value) return;

                if (SetProperty(ref _selectedOrderHeader, value))
                {
                    // 中文注释：选中项变化时，加载对应订单的行集合并绑定到订单头，
                    // 这样行改动时能自动更新订单头的汇总字段。
                    LoadOrderLinesForSelectedHeader();
                    RaisePropertyChanged(nameof(CanSave));
                    SaveCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 中文注释：加载选中订单对应的行集合。
        /// 这里暂时使用空实现，实际应该调 API 获取该订单 ID 对应的明细行。
        /// </summary>
        private void LoadOrderLinesForSelectedHeader()
        {
            if (_selectedOrderHeader == null)
            {
                CurrentOrderLines.BindHeader(null);
                CurrentOrderLines.LoadFromDto(null);
                return;
            }

            // 中文注释：将行集合重新绑定到当前选中的订单头，
            // 这样行里数量/金额变化时会自动同步到订单头的汇总字段。
            CurrentOrderLines.BindHeader(_selectedOrderHeader);

            // TODO: 根据 _selectedOrderHeader.Id 调 API 加载该订单的明细行。
            // 示例：
            // var lines = await _salesOrderApi.GetOrderLinesAsync(_selectedOrderHeader.Id);
            // CurrentOrderLines.LoadFromDto(lines);

            // 暂时先清空，后续补充 API 调用
            CurrentOrderLines.LoadFromDto(null);
        }

        private void OrderHeaders_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OrderHeaders.IsDirty) ||
                e.PropertyName == nameof(OrderHeaders.IsAllValid))
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }

        #endregion

        // ============================================================
        // 从表：当前选中订单的行集合
        // ============================================================
        #region 行集合模型

        public SalesOrderLineList CurrentOrderLines { get; }

        private SalesOrderLineModel _selectedLine;
        /// <summary>
        /// 从表当前选中行
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

        private void CurrentOrderLines_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CurrentOrderLines.IsDirty) ||
                e.PropertyName == nameof(CurrentOrderLines.IsAllValid))
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }

        #endregion

        // ============================================================
        // 保存按钮状态
        // ============================================================
        #region 保存按钮状态（CanSave）

        /// <summary>
        /// 中文注释：保存按钮是否可用。
        /// 需要满足：
        /// 1. 选中了订单头
        /// 2. 订单头有效
        /// 3. 行集合全部有效
        /// 4. 主表或从表存在脏数据
        /// </summary>
        public bool CanSave =>
            SelectedOrderHeader != null &&
            SelectedOrderHeader.IsValid &&
            CurrentOrderLines.IsAllValid &&
            (SelectedOrderHeader.State != ModelState.Unchanged || 
             OrderHeaders.IsDirty || 
             CurrentOrderLines.IsDirty);

        #endregion

        // ============================================================
        // 命令
        // ============================================================
        #region 命令

        public RelayCommand AddLineCommand { get; }
        public RelayCommand DeleteLineCommand { get; }
        public RelayCommand SaveCommand { get; }

        #endregion

        // ============================================================
        // 行操作
        // ============================================================
        #region 行操作

        private void AddLine()
        {
            if (SelectedOrderHeader == null)
            {
                _msg?.ShowWarningAsync("请先选中一个订单");
                return;
            }

            var line = new SalesOrderLineModel();
            line.MarkAsAdded();

            CurrentOrderLines.Items.Add(line);
            SelectedLine = line;
        }

        private void DeleteLine()
        {
            if (SelectedLine == null)
                return;

            CurrentOrderLines.DeleteLine(SelectedLine);
            SelectedLine = null;
        }

        #endregion

        // ============================================================
        // 保存订单
        // ============================================================
        #region 保存订单

        private async Task SaveAsync()
        {
            if (SelectedOrderHeader == null)
            {
                if (_msg != null)
                    await _msg.ShowWarningAsync("请先选中一个订单");
                return;
            }

            // 中文注释：先做异步校验，避免无效数据继续提交。
            var asyncError = await CurrentOrderLines.ValidateAllAsync();
            if (!string.IsNullOrEmpty(asyncError))
            {
                if (_msg != null)
                    await _msg.ShowWarningAsync(asyncError);
                return;
            }

            // 中文注释：获取当前行变更集合，后续可直接用于组装 DTO 并调用 API。
            var linesChangeSet = CurrentOrderLines.GetChangeSet();
            var headersChangeSet = OrderHeaders.GetChangeSet();

            // TODO: 使用 _salesOrderApi 组装并提交订单头与订单行数据。
            _ = _salesOrderApi;

            // 中文注释：当前阶段先提交本地状态，保证 View 与 ViewModel 的交互闭环。
            SelectedOrderHeader.CommitChanges();

            foreach (var line in linesChangeSet.AddList)
                line.CommitChanges();
            foreach (var line in linesChangeSet.UpdateList)
                line.CommitChanges();

            foreach (var header in headersChangeSet.AddList)
                header.CommitChanges();
            foreach (var header in headersChangeSet.UpdateList)
                header.CommitChanges();

            RaisePropertyChanged(nameof(CanSave));
            SaveCommand.RaiseCanExecuteChanged();

            // 中文注释：通知窗口以"保存成功"结果关闭。
            CloseWindow?.Invoke(true);
        }

        #endregion
    }
}
