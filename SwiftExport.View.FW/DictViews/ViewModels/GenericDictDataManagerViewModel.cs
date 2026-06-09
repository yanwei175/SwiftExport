using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Routing;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    /// <summary>
    /// 通用字典明细管理 ViewModel（仅英文名 + 中文名类枚举）
    /// 
    /// 适用场景：
    /// 1. 某个 DictCode 下只有 DataNameEn / DataNameZh / Sort / Enable / Remark 等基础字段
    /// 2. 通过路由注入 DictCode 后进行统一管理
    /// </summary>
    public class GenericDictDataManagerViewModel : ViewModelBaseEx, IDictRouteAwareViewModel
    {
        private readonly ISysDictDataApi _dictDataApi;
        private readonly IDialogService _msg;

        // ============= 路由上下文 =============
        #region 路由上下文
        private string _dictCode;
        public string DictCode
        {
            get => _dictCode;
            set => SetProperty(ref _dictCode, value);
        }

        private object _routeContext;
        public object RouteContext
        {
            get => _routeContext;
            private set => SetProperty(ref _routeContext, value);
        }

        private string _columnNameEn = "DataNameEn";
        public string ColumnNameEn
        {
            get => _columnNameEn;
            set => SetProperty(ref _columnNameEn, value);
        }

        private string _columnNameZh = "DataNameZh";
        public string ColumnNameZh
        {
            get => _columnNameZh;
            set => SetProperty(ref _columnNameZh, value);
        }

        private string _headerTitle = "字典明细管理";
        public string HeaderTitle
        {
            get => _headerTitle;
            set => SetProperty(ref _headerTitle, value);
        }

        /// <summary>
        /// 路由注入上下文：设置 DictCode 并加载数据。
        /// </summary>
        public void SetRouteContext(string dictCode, string dictDisplayName, object routeContext = null)
        {
            DictCode = dictCode;
            ColumnNameEn = string.IsNullOrWhiteSpace(dictDisplayName)
                ? "英文名称"
                : dictDisplayName + "英文";
            ColumnNameZh = string.IsNullOrWhiteSpace(dictDisplayName)
                ? "中文名称"
                : dictDisplayName + "中文";
            RouteContext = routeContext;
            HeaderTitle = string.IsNullOrWhiteSpace(dictDisplayName)
                ? "字典明细管理"
                : dictDisplayName + "（通用字典）";

            // 通过触发 DataGrid 等重新绑定或者明确抛出通知
            RaisePropertyChanged(nameof(HeaderTitle));
            RaisePropertyChanged(nameof(DictCode));
            RaisePropertyChanged(nameof(RouteContext));

            _ = LoadAsync();
        }
        #endregion

        // ============= 数据表 =============
        public DictDataList CurrentTable { get; }
        public ICollectionView ItemsView { get; private set; }

        // ============= 选中行 =============
        private DictDataModel _selectedItem;
        public DictDataModel SelectedItem
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

        // ============= 搜索 =============

        #region 搜索
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

        private bool FilterItem(DictDataModel item, string searchText)
        {
            return (item.DataNameEn?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.DataNameZh?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.Remark?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();

            Task.Delay(350, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                DispatcherProvider.BeginInvoke(() =>
                {
                    ItemsView.Filter = (obj) => string.IsNullOrWhiteSpace(text) || FilterItem((DictDataModel)obj, text);
                    ItemsView.Refresh();
                });
            }, TaskScheduler.Default);
        }

        #endregion

        #region 状态
        // ============= 状态 =============
        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ============= 命令 =============
        public RelayCommand CreateCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand<DictDataModel> DeleteCommand { get; }
        public RelayCommand<DictDataModel> OpenEditCommand { get; }
        public RelayCommand SaveEditCommand { get; }
        public RelayCommand CancelEditCommand { get; }

        // ============= 编辑弹窗 =============
        private bool _isEditDialogOpen;
        public bool IsEditDialogOpen
        {
            get => _isEditDialogOpen;
            set => SetProperty(ref _isEditDialogOpen, value);
        }

        private DictDataModel _editingItem;
        public DictDataModel EditingItem
        {
            get => _editingItem;
            set => SetProperty(ref _editingItem, value);
        }
        public GenericDictDataManagerViewModel()
        {

        }
        public GenericDictDataManagerViewModel(ISysDictDataApi dictDataApi, IDialogService dialogService) : this() 
        {
            _dictDataApi = dictDataApi;
            _msg = dialogService;

            CurrentTable = new DictDataList();
            ItemsView = CollectionViewSource.GetDefaultView(CurrentTable.Items);

            CreateCommand = new RelayCommand(Create);
            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            DeleteCommand = new RelayCommand<DictDataModel>(async item => await DeleteAsync(item), item => item != null && !item.IsSystem);
            OpenEditCommand = new RelayCommand<DictDataModel>(OpenEdit);
            SaveEditCommand = new RelayCommand(async () => await SaveEditAsync(), () => EditingItem != null);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditDialogOpen);
        }
        #endregion


        private void Create()
        {
            var model = new DictDataModel
            {
                Uid = Guid.Empty,
                DictCode = DictCode,
                DataNameEn = string.Empty,
                DataNameZh = string.Empty,
                IsEnabled = true,
                IsSystem = false,
                SortOrder = 0
            };

            EditingItem = model;
            IsEditDialogOpen = true;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }

        private async Task LoadAsync()
        {
            if (string.IsNullOrWhiteSpace(DictCode))
            {
                StatusMessage = "未注入 DictCode，无法加载。";
                CurrentTable.Clear();
                return;
            }

            using (BusyScope())
            {
                StatusMessage = "加载中...";
                try
                {
                    var rst = await _dictDataApi.GetByCodeAsync(DictCode);

                    DispatcherProvider.BeginInvoke(() =>
                    {
                        if (rst.Success && rst.Data != null)
                        {
                            CurrentTable.ReLoadRangeFromDtoList(rst.Data.ToList());
                            StatusMessage = $"共 {rst.Data.Count} 条记录";
                        }
                        else
                        {
                            CurrentTable.Clear();
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

        private void OpenEdit(DictDataModel item)
        {
            if (item == null) return;

            if (EditingItem == null)
            {
                EditingItem = new DictDataModel();
            }

            foreach (var prop in typeof(DictDataModel).GetProperties())
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

            if (!EditingItem.IsValid)
            {
                await _msg.ShowWarningAsync("请先完善必填字段。");
                return;
            }

            // 强制归属到当前路由字典编码，避免串数据
            EditingItem.DictCode = DictCode;

            var dto = EditingItem.Adapt<SysDictDataDto>();

            using (BusyScope())
            {
                StatusMessage = "保存中...";
                try
                {
                    if (EditingItem.Uid == Guid.Empty)
                    {
                        var addRst = await _dictDataApi.CreateAsync(dto);
                        if (!addRst.Success)
                        {
                            StatusMessage = "添加失败: " + addRst.Message;
                            await _msg.ShowWarningAsync("添加失败：" + addRst.Message);
                            return;
                        }
                    }
                    else
                    {
                        var updRst = await _dictDataApi.UpdateAsync(dto.Uid, dto);
                        if (!updRst.Success)
                        {
                            StatusMessage = "保存失败: " + updRst.Message;
                            await _msg.ShowWarningAsync("保存失败：" + updRst.Message);
                            return;
                        }
                    }

                    IsEditDialogOpen = false;
                    await LoadAsync();
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

        private async Task DeleteAsync(DictDataModel item)
        {
            if (item == null) return;

            if (item.Uid == Guid.Empty)
            {
                CurrentTable.Remove(item);
                return;
            }

            if (item.IsSystem)
            {
                await _msg.ShowWarningAsync("系统内置项不允许删除。");
                return;
            }

            var confirmed = await _msg.ShowConfirmAsync("确定删除该字典项吗？", "确认删除");
            if (!confirmed) return;

            using (BusyScope())
            {
                StatusMessage = "删除中...";
                try
                {
                    var rst = await _dictDataApi.DeleteAsync(item.Uid);
                    if (!rst.Success)
                    {
                        StatusMessage = "删除失败: " + rst.Message;
                        await _msg.ShowWarningAsync("删除失败：" + rst.Message);
                        return;
                    }

                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "删除异常: " + ex.Message;
                }
            }
        }
    }
}
