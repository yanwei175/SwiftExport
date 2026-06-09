using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class ProductColorManagerViewModel : ViewModelBaseEx
    {
        private readonly IProductColorApi _api;
        private readonly IDialogService _msg;

        // ============= 数据表 =============

        public ProductColorList CurrentTable { get; }
        public ICollectionView ItemsView { get; private set; }

        // ============= 选中行 =============

        private ProductColorModel _selectedItem;
        public ProductColorModel SelectedItem
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
                    DebounceFilter(value);
            }
        }

        private CancellationTokenSource _debounceCts;

        private bool FilterItem(ProductColorModel item, string searchText)
        {
            return (item.ColorCode?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.ColorNameEn?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.ColorNameZh?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.ColorCodeUnique?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
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
                    ItemsView.Filter = (obj) => string.IsNullOrWhiteSpace(text) || FilterItem((ProductColorModel)obj, text);
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
            var model = new ProductColorModel
            {
                Uid = Guid.Empty,
                ColorCodeUnique = "",
                ColorCode = "",
                ColorNameEn = "",
                ColorNameZh = ""
            };
            EditingItem = model;
            IsEditDialogOpen = true;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }
        #endregion
        #region 删除
        public bool CanDelete => SelectedItem != null;
        public RelayCommand<ProductColorModel> DeleteCommand { get; }
        private async Task DeleteAsync(ProductColorModel item)
        {

            if (item == null) return;
            var id = item.Uid;
            if (id == Guid.Empty)
            {
                CurrentTable.Remove(item);
                return;
            }

            var confirmed = await _msg.ShowConfirmAsync($"确定要删除选中的颜色 {item.ColorNameZh} 吗？", "确认删除");
            if (!confirmed) return;

            using (BusyScope())
            {
                StatusMessage = $"正在删除 {item.ColorNameZh} ...";
                try
                {
                    var rst = await _api.DeleteAsync(id);

                    if (!rst.Success)
                    {
                        StatusMessage = "删除失败: " + rst.Message;
                        await _msg.ShowWarningAsync("删除失败：" + rst.Message);
                        return;
                    }

                    StatusMessage = $"已删除 {item.ColorNameZh} ";
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

        // ============= 构造函数 =============

        #region 加载
        public ProductColorManagerViewModel(IProductColorApi api, IDialogService dialogService)
        {
            _api = api;
            _msg = dialogService;

            CurrentTable = new ProductColorList();
            ItemsView = CollectionViewSource.GetDefaultView(CurrentTable.Items);

            CreatCommand = new RelayCommand(Creat);
            DeleteCommand = new RelayCommand<ProductColorModel>(async (item) => await DeleteAsync(item), item => item != null);
            RefreshCommand = new RelayCommand(async () => await LoadAsync());

            OpenEditCommand = new RelayCommand<ProductColorModel>(OpenEdit);
            SaveEditCommand = new RelayCommand(async () => await SaveEditAsync(), () => EditingItem != null);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditDialogOpen);

            _ = LoadAsync();
        }

        // ============= 数据加载 =============

        private async Task LoadAsync()
        {
            using (BusyScope())
            {
                StatusMessage = "加载中...";
                try
                {

                    var rst = await _api.GetAllAsync();

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

        private ProductColorModel _editingItem;
        public ProductColorModel EditingItem
        {
            get => _editingItem;
            set => SetProperty(ref _editingItem, value);
        }

        public RelayCommand<ProductColorModel> OpenEditCommand { get; }
        private void OpenEdit(ProductColorModel model)
        {
            if (model == null) return;

            if (EditingItem == null)
            {
                EditingItem = new ProductColorModel();
            }

            foreach (var prop in typeof(ProductColorModel).GetProperties())
            {
                if (prop.CanRead && prop.CanWrite)
                    prop.SetValue(EditingItem, prop.GetValue(model));
            }
            SelectedItem = model;
            IsEditDialogOpen = true;
            SaveEditCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }

        public RelayCommand SaveEditCommand { get; }
        private async Task SaveEditAsync()
        {
            if (!IsEditDialogOpen) return;
            if (EditingItem == null) return;
            
            if (!EditingItem.IsValid)
            {
                await _msg.ShowWarningAsync("存在未填写完整的行！\n\n【颜色唯一代码】和【颜色代码】为必填项，请补充完整后再保存。");
                return;
            }
            var markCode = EditingItem.ColorCodeUnique;
            var dto = EditingItem.Adapt<ProductColorDto>();
            using (BusyScope())
            {
                StatusMessage = "保存中...";
                try
                {
                    if (EditingItem.Uid == Guid.Empty)
                    {
                        var addRst = await _api.CreateAsync(dto);
                        if (!addRst.Success)
                        {
                            StatusMessage = "添加失败: " + addRst.Message;
                            await _msg.ShowWarningAsync("添加失败：" + addRst.Message);
                            return;
                        }
                    }
                    else
                    {
                        var rst = await _api.UpdateAsync(dto.Uid, dto);
                        if (!rst.Success)
                        {
                            StatusMessage = "保存失败: " + rst.Message;
                            await _msg.ShowWarningAsync("保存失败：" + rst.Message);
                            return;
                        }
                        StatusMessage = "保存成功";
                    }

                    IsEditDialogOpen = false;
                    await LoadAsync();
                    DispatcherProvider.BeginInvoke(() =>
                    {
                        foreach (var item in CurrentTable.Items)
                        {
                            if (item.ColorCodeUnique == markCode)
                                SelectedItem = item; return;
                        }
                    });
                 



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
    }
}
