using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SystemViews.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SwiftExport.View.FW.SystemViews.ViewModels
{
    /// <summary>
    /// 货币管理 ViewModel
    /// 
    /// 核心功能：
    /// 1. 标准 CRUD（新增/编辑/删除/刷新）
    /// 2. ComboBox 切换本位币：
    ///    - 切换到非 DB 本位币 → 清空列表中所有行的汇率（因为基准已变，原汇率无效）
    ///    - 切换回 DB 原始本位币 → 从快照恢复整个列表（汇率复原）
    /// </summary>
    public class SysDicCurrencyViewModel : ViewModelBaseEx
    {
        private readonly ISysDicCurrencyApi _currencyApi;
        private readonly IDialogService _msg;

        // ================================================================
        // DB 原始快照（加载后存档，用于切换回本位币时恢复汇率）
        // ================================================================

        /// <summary>从数据库加载的原始 DTO 快照，切换回 DB 本位币时用于恢复</summary>
        private List<SysDicCurrencyDto> _snapshotDtos = new List<SysDicCurrencyDto>();

        /// <summary>数据库中原始本位币的货币代码（加载时记录一次，不再改变）</summary>
        private string _dbBaseCurrencyCode = string.Empty;


        private string _currentBaseNameZh;
        public string CurrentBaseNameZh
        {
            get => _currentBaseNameZh;
            set => SetProperty(ref _currentBaseNameZh, value);
        }

        // ================================================================
        // DataGrid 数据绑定
        // ================================================================

        /// <summary>货币列表（绑定 DataGrid.ItemsSource = CurrentTable.Items）</summary>
        public CurrencyList CurrentTable { get; }

        public ICollectionView ItemsView { get; private set; }

        // ================================================================
        // 选中行
        // ================================================================

        private CurrencyModel _selectedItem;
        public CurrencyModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    DeleteCommand.RaiseCanExecuteChanged();
                }
            }
        }


        // ================================================================
        // ComboBox：本位币切换器
        // ================================================================

        #region 本币切换Combobox数据源

        /// <summary>ComboBox 数据源 —— 所有货币的简单列表（代码+中文名）</summary>
        private List<SysDicCurrencyDto> _currencyOptions = new List<SysDicCurrencyDto>();
        public List<SysDicCurrencyDto> CurrencyOptions
        {
            get => _currencyOptions;
            private set => SetProperty(ref _currencyOptions, value);
        }

        /// <summary>当前 UI 选定的本位币（绑定 ComboBox.SelectedItem）</summary>
        private SysDicCurrencyDto _selectedBaseCurrency;
        public SysDicCurrencyDto SelectedBaseCurrency
        {
            get => _selectedBaseCurrency;
            set
            {
                if (SetProperty(ref _selectedBaseCurrency, value))
                {
                    // 更新警告标签可见性
                    RaisePropertyChanged(nameof(IsBaseSwitched));
                }
            }
        }

        /// <summary>
        /// 当前选中的本位币与 DB 原始值不一致时为 true，
        /// XAML 绑定此属性控制警告标签的显示
        /// </summary>
        public bool IsBaseSwitched =>
            _selectedBaseCurrency != null &&
            !string.IsNullOrEmpty(_dbBaseCurrencyCode) &&
            _selectedBaseCurrency.CurrencyCode != _dbBaseCurrencyCode;


        // ================================================================
        // 主币种编辑弹窗
        // ================================================================
        private bool _isEditBaseSwitchDialogOpen;
        public bool IsEditBaseSwitchDialogOpen
        {
            get => _isEditBaseSwitchDialogOpen;
            set => SetProperty(ref _isEditBaseSwitchDialogOpen, value);
        }
        private void OpenEditBaseSwitch()
        {
            _selectedBaseCurrency = CurrencyOptions.FirstOrDefault(x => x.CurrencyCode == _dbBaseCurrencyCode);
            RaisePropertyChanged(nameof(SelectedBaseCurrency));
            IsEditBaseSwitchDialogOpen = true;
        }

        private void CancelEditBaseSwitch()
        {
            IsEditBaseSwitchDialogOpen = false;
        }


        private async Task SaveEditBaseSwitchAsync()
        {
            if (SelectedBaseCurrency == null) return;

            if (SelectedBaseCurrency.CurrencyCode == _dbBaseCurrencyCode)
            {
                IsEditBaseSwitchDialogOpen = false;
                return;
            }

            var confirm = await _msg.ShowConfirmAsync($"确定要将 [{SelectedBaseCurrency.CurrencyNameZh}] 设置为主币种吗？修改币种会重置汇率。", "确认切换主币种");
            
            if (!confirm) return;

            using (BusyScope())
            {
                StatusMessage = "保存主币种中...";
                try
                {
                    var dto = SelectedBaseCurrency;
                    dto.IsBase = true;
                    // 后端接口调用校验和保存
                    var updRst = await _currencyApi.SetNewBaseCurrency(dto);
                    if (!updRst.Success)
                    {
                        StatusMessage = "保存主币种失败: " + updRst.Message;
                        await _msg.ShowWarningAsync("保存主币种失败：" + updRst.Message);
                        return;
                    }

                    IsEditBaseSwitchDialogOpen = false;
                    await LoadAsync(); // 保存成功后重新拉取
                }
                catch (Exception ex)
                {
                    StatusMessage = "保存主币种异常: " + ex.Message;
                }
            }
        }




        #endregion

        // ================================================================
        // 搜索防抖
        // ================================================================

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    DebounceFilter(value);
            }
        }

        private CancellationTokenSource _debounceCts;

        private bool FilterItem(CurrencyModel item, string text)
        {
            return (item.CurrencyCode?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.CurrencyNameZh?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.CurrencyNameEn?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.CurrencySymbol?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            Task.Delay(300, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                DispatcherProvider.BeginInvoke(() =>
                {
                    ItemsView.Filter = obj => string.IsNullOrWhiteSpace(text) || FilterItem((CurrencyModel)obj, text);
                    ItemsView.Refresh();
                });
            }, TaskScheduler.Default);
        }

        // ================================================================
        // 状态栏
        // ================================================================

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
 
        }

        // ================================================================
        // 命令
        // ================================================================

        public RelayCommand CreateCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand InvertCheckCommand { get; }
        public RelayCommand<CurrencyModel> DeleteCommand { get; }
        public RelayCommand<CurrencyModel> OpenEditCommand { get; }
        public RelayCommand SaveEditCommand { get; }
        public RelayCommand CancelEditCommand { get; }
        public RelayCommand SaveAllCommand { get; }
        public RelayCommand OpenEditBaseSwitchCommand { get; }
        public RelayCommand SaveEditBaseSwitchCommand { get; }
        public RelayCommand CancelEditBaseSwitchCommand { get; }

        // ================================================================
        // 编辑弹窗
        // ================================================================

        #region 编辑货币弹窗

        private bool _isEditDialogOpen;
        public bool IsEditDialogOpen
        {
            get => _isEditDialogOpen;
            set => SetProperty(ref _isEditDialogOpen, value);
        }


        private CurrencyModel _editingItem;
        public CurrencyModel EditingItem
        {
            get => _editingItem;
            set => SetProperty(ref _editingItem, value);
        }

        // ================================================================
        // 编辑弹窗（拷贝副本，避免直接污染 DataGrid 行）
        // ================================================================

        private void OpenEdit(CurrencyModel item)
        {
            if (item == null) return;
            // 拷贝副本，避免直接污染 DataGrid 行（不使用 C# 8.0 的 ??= 语法）
            if (EditingItem == null)
                EditingItem = new CurrencyModel();
            foreach (var prop in typeof(CurrencyModel).GetProperties())
            {
                if (prop.CanRead && prop.CanWrite)
                    prop.SetValue(EditingItem, prop.GetValue(item));
            }
            SelectedItem = item;
            IsEditDialogOpen = true;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }

        private async Task SaveEditAsync()
        {
            if (!IsEditDialogOpen || EditingItem == null) return;

            var dto = EditingItem.Adapt<SysDicCurrencyDto>();
            using (BusyScope())
            {
                StatusMessage = "保存中...";
                try
                {
                    if (EditingItem.Id <= 0)
                    {
                        var addRst = await _currencyApi.CreateAsync(dto);
                        if (!addRst.Success)
                        {
                            StatusMessage = "新增失败: " + addRst.Message;
                            await _msg.ShowWarningAsync("新增失败：" + addRst.Message);
                            return;
                        }
                    }
                    else
                    {
                        var updRst = await _currencyApi.UpdateAsync(dto.Id, dto);
                        if (!updRst.Success)
                        {
                            StatusMessage = "保存失败: " + updRst.Message;
                            await _msg.ShowWarningAsync("保存失败：" + updRst.Message);
                            return;
                        }
                    }

                    IsEditDialogOpen = false;
                    await LoadAsync(); // 重新加载以同步 ID 回填及快照
                }
                catch (Exception ex)
                {
                    StatusMessage = "保存异常: " + ex.Message;
                }
            }
        }

        private void CancelEdit()
        {
            IsEditDialogOpen = false;
            EditingItem = null;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }


        #endregion
        // ================================================================
        // 构造函数
        // ================================================================

        public SysDicCurrencyViewModel(ISysDicCurrencyApi currencyApi, IDialogService dialogService)
        {
            _currencyApi = currencyApi;
            _msg = dialogService;

            CurrentTable = new CurrencyList();
            ItemsView = CollectionViewSource.GetDefaultView(CurrentTable.Items);

            CreateCommand = new RelayCommand(Create);
            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            InvertCheckCommand = new RelayCommand(InvertCheck);
            DeleteCommand = new RelayCommand<CurrencyModel>(
                async item => await DeleteAsync(item),
                item => item != null && !item.IsSystem);

            OpenEditCommand = new RelayCommand<CurrencyModel>(OpenEdit);
            SaveEditCommand = new RelayCommand(async () => await SaveEditAsync(), () => EditingItem != null);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditDialogOpen);

            OpenEditBaseSwitchCommand = new RelayCommand(OpenEditBaseSwitch);
            SaveEditBaseSwitchCommand = new RelayCommand(async () => await SaveEditBaseSwitchAsync());
            CancelEditBaseSwitchCommand = new RelayCommand(CancelEditBaseSwitch);


            _ = LoadAsync();
        }

        // ================================================================
        // 数据加载
        // ================================================================

        /// <summary>
        /// 加载所有货币数据：
        /// 1. 记录 DB 原始本位币代码
        /// 2. 保存快照（用于切换回本位币时恢复汇率）
        /// 3. 填充 ComboBox 数据源
        /// 4. 初始化 ComboBox 选中项（DB 本位币）
        /// </summary>
        private async Task LoadAsync()
        {
            using (BusyScope())
            {
                StatusMessage = "加载中...";
                try
                {
                    var rst = await _currencyApi.GetAllAsync();

                    DispatcherProvider.BeginInvoke(() =>
                    {
                        if (!rst.Success || rst.Data == null)
                        {
                            StatusMessage = "加载失败: " + rst.Message;
                            return;
                        }

                        var dtoList = rst.Data.ToList();

                        // ── 1. 记录并保存快照 ──
                        _snapshotDtos = dtoList.Select(d => d.Adapt<SysDicCurrencyDto>()).ToList();

                        var dbBase = dtoList.FirstOrDefault(x => x.IsBase);
                        _dbBaseCurrencyCode = dbBase?.CurrencyCode ?? string.Empty;
                        CurrentBaseNameZh = dbBase?.CurrencyNameZh ?? string.Empty;

                        // ── 2. 填充 DataGrid ──
                        CurrentTable.ReLoadRangeFromDtoList(dtoList);

                        // ── 3. 填充 ComboBox 选项 ──
                        CurrencyOptions = dtoList.Select(d => d.Adapt<SysDicCurrencyDto>()).ToList();

                        // ── 4. 初始化 ComboBox 选中项（用字段直接赋值，跳过 OnBaseCurrencySwitched）
                        _selectedBaseCurrency = CurrencyOptions.FirstOrDefault(x => x.CurrencyCode == _dbBaseCurrencyCode);
                        RaisePropertyChanged(nameof(SelectedBaseCurrency));

                        StatusMessage = $"共 {dtoList.Count} 条记录，本位币：{_dbBaseCurrencyCode}";
                    });
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常: " + ex.Message;
                }
            }
        }



        // ================================================================
        // 新增
        // ================================================================

        private void Create()
        {
            
            var model = new CurrencyModel { Id = 0 };
            model.MarkAsAdded();
            EditingItem = model;
            IsEditDialogOpen = true;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();


        }



        // ================================================================
        // 删除
        // ================================================================

        private async Task DeleteAsync(CurrencyModel item)
        {
            if (item == null) return;

            if (item.Id <= 0)
            {
                CurrentTable.Remove(item);
                return;
            }

            var confirmed = await _msg.ShowConfirmAsync($"确定要删除货币 [{item.CurrencyCode}] {item.CurrencyNameZh} 吗？", "确认删除");
            if (!confirmed) return;

            using (BusyScope())
            {
                StatusMessage = $"正在删除 {item.CurrencyCode} ...";
                try
                {
                    var rst = await _currencyApi.DeleteAsync(item.Id);
                    if (!rst.Success)
                    {
                        StatusMessage = "删除失败: " + rst.Message;
                        await _msg.ShowWarningAsync("删除失败：" + rst.Message);
                        return;
                    }

                    StatusMessage = $"已删除 {item.CurrencyCode}";
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "删除异常: " + ex.Message;
                }
            }
        }



        // ================================================================
        // 反选
        // ================================================================

        private void InvertCheck()
        {
            foreach (var item in CurrentTable.Items)
                item.IsChecked = !item.IsChecked;
        }
    }
}
