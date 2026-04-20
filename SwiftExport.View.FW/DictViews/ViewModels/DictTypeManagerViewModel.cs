using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

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

        // ============= 数据表 =============

        /// <summary>
        /// 字典类型列表（绑定 DataGrid.ItemsSource = CurrentTable.Items）
        /// </summary>
        public DictTypeList CurrentTable { get; }
        public ICollectionView ItemsView { get; private set; }

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

        #region 搜索
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

        private bool FilterItem(DictTypeModel item, string searchText)
        {
            return (item.DictCode?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.DictName?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.GroupName?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.TargetViewKey?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.Remark?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();

            Task.Delay(350, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                // 中文注释：过滤刷新统一走线程安全调度，避免跨线程触发 ICollectionView 异常。
                DispatcherProvider.BeginInvoke(() =>
                {
                    ItemsView.Filter = (obj) => string.IsNullOrWhiteSpace(text) || FilterItem((DictTypeModel)obj, text);
                    ItemsView.Refresh();
                });
            }, TaskScheduler.Default);
        }

        #endregion

        // ============= 状态 =============

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }


        // ============= 命令 =============
        #region 创建
        public RelayCommand CreatCommand { get; }
        private void Creat()
        {
            var model = new DictTypeModel
            {
                Id = 0, // 新增行 ID 默认为 0 或负数，表示未持久化
                DictCode = "",
                DictName = "",
                IsSystem = false
            };
            EditingItem = model;
            IsEditDialogOpen = true;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }
        #endregion
        #region 删除
        public bool CanDelete => SelectedItem != null && !SelectedItem.IsSystem;
        public RelayCommand<DictTypeModel> DeleteCommand { get; }
        private async Task DeleteAsync(DictTypeModel item)
        {

            if (item == null) return;
            var id = item.Id;
            if (id <= 0)
            {
                CurrentTable.Remove(item);
                return;
            }

            var confirmed = await _msg.ShowConfirmAsync($"确定要删除选中的 {item.DictName} 字典类型吗？", "确认删除");
            if (!confirmed) return;

            using (BusyScope())
            {
                StatusMessage = $"正在删除 {item.DictName} ...";
                try
                {
                    var rst = await _dictTypeApi.DeleteAsync(id);

                    if (!rst.Success)
                    {
                        StatusMessage = "删除失败: " + rst.Message;
                        await _msg.ShowWarningAsync("删除失败：" + rst.Message);
                        return;
                    }

                    StatusMessage = $"已删除 {item.DictName} ";
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "删除异常: " + ex.Message;
                }
            }
        }

        #endregion

        public RelayCommand RefreshCommand { get; }
        public RelayCommand InvertCheckCommand { get; }

        // ============= 构造函数 =============


        #region 加载
        public DictTypeManagerViewModel(ISysDictTypeApi dictTypeApi, IDialogService dialogService)
        {
            _dictTypeApi = dictTypeApi;
            _msg = dialogService;

            CurrentTable = new DictTypeList();
            ItemsView = CollectionViewSource.GetDefaultView(CurrentTable.Items);

            CreatCommand = new RelayCommand(Creat);
            DeleteCommand = new RelayCommand<DictTypeModel>(async (item) => await DeleteAsync(item), item => item != null && !item.IsSystem);
            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            InvertCheckCommand = new RelayCommand(InvertCheck);

            OpenEditCommand = new RelayCommand<DictTypeModel>(OpenEdit);
            SaveEditCommand = new RelayCommand(async () => await SaveEditAsync(), () => EditingItem != null);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditDialogOpen);

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

        #endregion



        #region 编辑
        private bool _isEditDialogOpen;
        public bool IsEditDialogOpen
        {
            get => _isEditDialogOpen;
            set => SetProperty(ref _isEditDialogOpen, value);
        }

        private DictTypeModel _editingItem;
        public DictTypeModel EditingItem
        {
            get => _editingItem;
            set => SetProperty(ref _editingItem, value);
        }

        public RelayCommand<DictTypeModel> OpenEditCommand { get; }
        private void OpenEdit(DictTypeModel dictTypeModel)
        {
            if (dictTypeModel == null) return;

            if (EditingItem == null)
            {
                EditingItem = new DictTypeModel();
            }

            //拷贝一份数据到 EditingItem，避免直接修改 SelectedItem 导致表格行变脏
            foreach (var prop in typeof(DictTypeModel).GetProperties())
            {
                if (prop.CanRead && prop.CanWrite)
                    prop.SetValue(EditingItem, prop.GetValue(dictTypeModel));
            }
            SelectedItem = dictTypeModel;
            IsEditDialogOpen = true;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }

        public RelayCommand SaveEditCommand { get; }
        private async Task SaveEditAsync()
        {
            if (!IsEditDialogOpen) return;
            if (EditingItem == null) return;
            var dto = EditingItem.Adapt<SysDictTypeDto>();
            using (BusyScope())
            {
                StatusMessage = "保存中...";
                try
                {
                    if (EditingItem.Id <= 0)
                    {
                        // 新增
                        var addRst = await _dictTypeApi.CreateAsync(dto);
                        if (!addRst.Success)
                        {
                            StatusMessage = "添加失败: " + addRst.Message;
                            await _msg.ShowWarningAsync("添加失败：" + addRst.Message);
                            return;
                        }
                    }
                    else
                    {
                        var rst = await _dictTypeApi.UpdateAsync(dto.Id, dto);
                        if (!rst.Success)
                        {
                            StatusMessage = "保存失败: " + rst.Message;
                            await _msg.ShowWarningAsync("保存失败：" + rst.Message);
                            return;
                        }
                        StatusMessage = "保存成功";
                    }

                    IsEditDialogOpen = false;
                    // 重新加载以同步最新服务端数据（ID 回填等）
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "保存异常: " + ex.Message;
                }
            }
        }

        public RelayCommand CancelEditCommand { get; }
        private void CancelEdit()
        {
            IsEditDialogOpen = false;
            EditingItem = null;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }






        #endregion




        // ============= 删除 =============

        /// <summary>
        /// 删除选中/勾选行
        /// - 新增行（未持久化）：直接从列表移除
        /// - 已持久化行：调用 API 删除后重新加载
        /// </summary>


        // ============= 反选 =============

        private void InvertCheck()
        {
            foreach (var item in CurrentTable.Items)
                item.IsChecked = !item.IsChecked;
        }


    }
}
