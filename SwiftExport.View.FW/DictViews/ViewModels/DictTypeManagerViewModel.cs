using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Services;
using SwiftExport.View.FW.SupplierViews.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    /// <summary>
    /// 字典类型管理 ViewModel（独立窗体）
    /// 使用 DictTypeList（BaseListModel 派生）驱动 DataGrid，
    /// 天然支持脏数据追踪、验证、勾选、批量保存
    /// </summary>
    public class DictTypeManagerViewModel : ViewModelBaseEx
    {
        private readonly ISysDictTypeApi _dictTypeApi;
        private readonly IDialogService _msg;
        private readonly IDictCacheService _dictCacheService;
        private readonly DispatcherTimer _搜索防抖计时器;

        // ============= 数据表 =============

        /// <summary>
        /// 字典类型列表（绑定 DataGrid.ItemsSource = CurrentTable.Items）
        /// </summary>
        public DictTypeList CurrentTable { get; }

        // ============= 选中行 =============

        private DictTypeModel _selectedItem;
        public DictTypeModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    RaisePropertyChanged(nameof(CanDelete));
                    DeleteCommand.RaiseCanExecuteChanged();
                }
            }
        }

        // ============= 搜索 =============

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    //防抖搜索开始();
                DebounceFilter(value); // 触发本地过滤，不发请求！
            }
        }

        private CancellationTokenSource _debounceCts;

        private  bool FilterItem(SupplierModel item, string searchText)
        {
            return (item.SupplierCode?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();

            Task.Delay(350, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                // 中文注释：过滤刷新统一走线程安全调度，避免跨线程触发 ICollectionView 异常。
                DispatcherProvider.Invoke(() =>
                {
                    //CurrentTable.Items.Filter = (obj) => string.IsNullOrWhiteSpace(text) || FilterItem((TModel)obj, text);
                    //CurrentTable.Items.Refresh();
                });
            }, TaskScheduler.Default);
        }



        // ============= 搜索防抖 =============

        private void 防抖搜索开始()
        {
            _搜索防抖计时器.Stop();
            _搜索防抖计时器.Start();
        }

        private void 搜索防抖计时器_Tick(object sender, EventArgs e)
        {
            _搜索防抖计时器.Stop();
        }





        // ============= 状态 =============

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ============= 命令可用性 =============

        /// <summary>
        /// 有脏数据时才能保存
        /// </summary>
        public bool CanSave => CurrentTable.IsDirty;

        /// <summary>
        /// 选中行存在、且不是系统内置时才能删除
        /// </summary>
        public bool CanDelete => SelectedItem != null && !SelectedItem.IsSystem;

        // ============= 命令 =============

        public RelayCommand AddCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand InvertCheckCommand { get; }

        // ============= 构造函数 =============

        public DictTypeManagerViewModel(
            ISysDictTypeApi dictTypeApi,
            IDialogService dialogService,
            IDictCacheService dictCacheService)
        {
            _dictTypeApi = dictTypeApi;
            _msg = dialogService;
            _dictCacheService = dictCacheService;

            CurrentTable = new DictTypeList();

            AddCommand       = new RelayCommand(AddRow);
            SaveCommand      = new RelayCommand(async () => await SaveAsync(), () => CanSave);
            DeleteCommand    = new RelayCommand(async () => await DeleteAsync(), () => CanDelete);
            RefreshCommand   = new RelayCommand(async () => await LoadAsync());
            InvertCheckCommand = new RelayCommand(InvertCheck);

            // 监听表格状态变化 → 刷新命令可用性
            CurrentTable.ListStateChanged += () =>
            {
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(CanDelete));
                DeleteCommand.RaiseCanExecuteChanged();
            };

            _搜索防抖计时器 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _搜索防抖计时器.Tick += 搜索防抖计时器_Tick;

            _ = LoadAsync();
        }

        // ============= 数据加载 =============

        /// <summary>
        /// 加载字典类型列表（分页 + 搜索），仅通过 Contracts 层的 API 接口与后端交互
        /// </summary>
        private async Task LoadAsync()
        {
            using (BusyScope())
            {
                StatusMessage = "加载中...";
                try
                {

                    var rst = await _dictTypeApi.GetAllAsync();

                    DispatcherProvider.BeginInvoke(() =>
                    {
                        if (rst.Success && rst.Data != null)
                        {
                            CurrentTable.ReLoadRangeFromDtoList(rst.Data.ToList());
                            StatusMessage = $"共 {rst.Data.Count} 条记录";
                        }
                        else
                        {
                            StatusMessage = "加载失败: " + rst.Message;
                        }
                    });
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常: " + ex.Message;
                }
            }
        }

        // ============= 新增 =============

        /// <summary>
        /// 在表格头部插入新行
        /// </summary>
        private void AddRow()
        {
            using (BusyScope())
            {
                var model = new DictTypeModel
                {
                    IsSystem = false,
                    SortOrder = 0
                };
                model.MarkAsAdded();
                CurrentTable.AddModelToFirst(model);
                StatusMessage = "已添加新行，请填写字典编码和名称后保存。";
            }
        }

        // ============= 保存 =============

        /// <summary>
        /// 批量保存脏数据（新增 / 修改 / 删除）
        /// </summary>
        private async Task SaveAsync()
        {
            // 验证所有脏行
            var dirtyItems = CurrentTable.Items
                .Where(x => x.State == ModelState.Added || x.State == ModelState.Modified)
                .ToList();
            foreach (var item in dirtyItems)
            {
                var fieldErr = item[nameof(DictTypeModel.DictCode)];
                if (!string.IsNullOrEmpty(fieldErr))
                {
                    await _msg.ShowWarningAsync($"保存失败：{fieldErr}");
                    return;
                }
                fieldErr = item[nameof(DictTypeModel.DictName)];
                if (!string.IsNullOrEmpty(fieldErr))
                {
                    await _msg.ShowWarningAsync($"保存失败：{fieldErr}");
                    return;
                }
            }

            // 检查编码重复（仅活跃行）
            var activeItems = CurrentTable.Items
                .Where(x => x.State != ModelState.Deleted)
                .ToList();
            var dup = activeItems
                .GroupBy(x => (x.DictCode ?? string.Empty).Trim().ToUpper())
                .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1);
            if (dup != null)
            {
                await _msg.ShowWarningAsync($"保存失败：字典编码【{dup.Key}】重复，请修正后再保存。");
                return;
            }

            using (BusyScope())
            {
                StatusMessage = "保存中...";
                try
                {
                    // GetChangeSet 返回 BatchRequestGeneric<DictTypeModel>（Model层）
                    // 用 Mapster Adapt 转为 DTO 再发给后端
                    var changeSet = CurrentTable.GetChangeSet();
                    var batch = new BatchRequestGeneric<SysDictTypeDto>
                    {
                        AddList      = changeSet.AddList?.Adapt<System.Collections.Generic.List<SysDictTypeDto>>(),
                        UpdateList   = changeSet.UpdateList?.Adapt<System.Collections.Generic.List<SysDictTypeDto>>(),
                        DeleteIdList = changeSet.DeleteIdList
                    };
                    var rst = await _dictTypeApi.BatchSaveAsync(batch);

                    if (!rst.Success)
                    {
                        StatusMessage = "保存失败: " + rst.Message;
                        await _msg.ShowWarningAsync("保存失败：" + rst.Message);
                        return;
                    }
                    StatusMessage = "保存成功";

                    // 刷新字典缓存（Fire-and-forget，不阻塞 UI）
                    _ = _dictCacheService.RefreshAllAsync();

                    // 重新加载以同步最新服务端数据（ID 回填等）
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "保存异常: " + ex.Message;
                }
            }
        }

        // ============= 删除 =============

        /// <summary>
        /// 删除选中/勾选行
        /// - 新增行（未持久化）：直接从列表移除
        /// - 已持久化行：调用 API 删除后重新加载
        /// </summary>
        private async Task DeleteAsync()
        {
            if (SelectedItem == null) return;

            // 勾选行批量删除优先，否则删除选中行
            var toDelete = CurrentTable.GetCheckedItems();
            if (toDelete.Count == 0)
                toDelete.Add(SelectedItem);

            // 过滤掉系统内置（且已持久化）的行
            var systemItems = toDelete.Where(x => x.IsSystem && x.Id > 0).ToList();
            if (systemItems.Any())
            {
                await _msg.ShowWarningAsync($"以下系统内置字典类型无法删除：{string.Join(", ", systemItems.Select(x => x.DictCode))}");
                toDelete = toDelete.Except(systemItems).ToList();
                if (toDelete.Count == 0) return;
            }

            // 1. 新增行（未保存过）直接从列表移除，不需要确认
            var newRows = toDelete.Where(x => x.Id <= 0).ToList();
            foreach (var item in newRows)
            {
                CurrentTable.Remove(item);
                toDelete.Remove(item);
            }

            // 2. 已持久化行调用后端 API 批量删除
            if (toDelete.Count == 0) return;

            var confirmed = await _msg.ShowConfirmAsync($"确定要删除选中的 {toDelete.Count} 条字典类型吗？", "确认删除");
            if (!confirmed) return;

            using (BusyScope())
            {
                StatusMessage = $"正在删除 {toDelete.Count} 条记录...";
                try
                {
                    // 构建仅含 DeleteIdList 的批量请求
                    var batch = new BatchRequestGeneric<SysDictTypeDto>
                    {
                        DeleteIdList = toDelete.Select(x => x.Id).ToList()
                    };
                    var rst = await _dictTypeApi.BatchSaveAsync(batch);

                    if (!rst.Success)
                    {
                        StatusMessage = "删除失败: " + rst.Message;
                        await _msg.ShowWarningAsync("删除失败：" + rst.Message);
                        return;
                    }

                    StatusMessage = $"已删除 {toDelete.Count} 条记录";
                    _ = _dictCacheService.RefreshAllAsync();
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "删除异常: " + ex.Message;
                }
            }
        }

        // ============= 反选 =============

        private void InvertCheck()
        {
            foreach (var item in CurrentTable.Items)
                item.IsChecked = !item.IsChecked;
        }

 
    }
}
