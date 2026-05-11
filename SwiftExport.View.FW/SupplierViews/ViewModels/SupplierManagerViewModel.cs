using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SupplierViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExport.View.FW.SupplierViews.ViewModels
{
    /// <summary>
    /// 供应商管理页签枚举。
    /// </summary>
    public enum SupplierManagerTabs
    {
        SupplierList = 0,
        SupplierProducts = 1
    }

    /// <summary>
    /// 供应商管理页 ViewModel。
    /// 重构目标：脱离 BaseSmallDataManagerViewModel，改为组合 SupplierListModel + 显式命令编排。
    /// </summary>
    public class SupplierManagerViewModel : ViewModelBaseEx
    {
        private readonly ISupplierApi _myApi;
        private readonly IDialogService _msg;
        private readonly SupplierListModel _list;
        private readonly Dispatcher _dispatcher;

        private bool _isLoading;
        private string _statusMessage = "准备就绪";

        private System.Threading.CancellationTokenSource _debounceCts;

        // 注入 API + 列表模型 + 对话服务
        public SupplierManagerViewModel(ISupplierApi api, IDialogService dialogService)
        {
            _msg = dialogService;
            _myApi = api;
            _dispatcher = DispatcherProvider.UI ?? Dispatcher.CurrentDispatcher;

            _list = new SupplierListModel();
            _list.ListStateChanged += OnListStateChanged;

            // DataGrid 绑定入口：ItemsView（对齐原 XAML）
            ItemsView = CollectionViewSource.GetDefaultView(_list.Items);

            AddSupplierCommand = new RelayCommand(AddSupplier);
            BatchSaveSupplierAsyncCommand = new RelayCommand(async () => await BatchSaveSupplierAsync(), () => HasChanges && !IsLoading);
            LoadSupplierAsyncCommand = new RelayCommand(async () => await LoadSupplierAsync(), () => !IsLoading);
            DeleteCheckedCommand = new RelayCommand(async () => await DeleteCheckedAsync(), () => _list.CheckedCount > 0 && !IsLoading);

            InvertCheckSupplierCommand = new RelayCommand(() =>
            {
                _list.InvertCheckAll();
                RaiseListFacadeProperties();
            });

            _ = LoadSupplierAsync();
        }

        private void OnListStateChanged()
        {
            RaiseListFacadeProperties();
        }

        private void RaiseListFacadeProperties()
        {
            RaisePropertyChanged(nameof(AllItems));
            RaisePropertyChanged(nameof(HasChanges));
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(TotalSupplierCount));
            RaisePropertyChanged(nameof(PageSupplierIndex));
            RaisePropertyChanged(nameof(PageSupplierSize));
            RaisePropertyChanged(nameof(CheckedCount));

            BatchSaveSupplierAsyncCommand.RaiseCanExecuteChanged();
            LoadSupplierAsyncCommand.RaiseCanExecuteChanged();
            DeleteCheckedCommand.RaiseCanExecuteChanged();
        }

        private SupplierManagerTabs _activeTab;
        /// <summary>
        /// 当前激活页签。
        /// </summary>
        public SupplierManagerTabs ActiveTab
        {
            get => _activeTab;
            set => SetProperty(ref _activeTab, value);
        }

        private SupplierModel _selectedSupplier;
        /// <summary>
        /// 当前选中供应商。
        /// </summary>
        public SupplierModel SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetProperty(ref _selectedSupplier, value);
        }

        /// <summary>
        /// DataGrid 显示集合（兼容原 XAML 绑定名 AllItems）。
        /// </summary>
        public ObservableCollection<SupplierModel> AllItems => _list.Items;

        /// <summary>
        /// DataGrid 视图（用于本地过滤）。
        /// </summary>
        public ICollectionView ItemsView { get; }

        /// <summary>
        /// 全选状态（供 CheckBoxDataGridTemplateColumn 绑定）。
        /// </summary>
        public bool IsAllChecked
        {
            get => _list.IsAllChecked;
            set
            {
                _list.IsAllChecked = value;
                RaiseListFacadeProperties();
            }
        }

        /// <summary>
        /// 已勾选条数。
        /// </summary>
        public int CheckedCount => _list.CheckedCount;

        /// <summary>
        /// 是否存在未保存变更。
        /// </summary>
        public bool HasChanges => _list.IsDirty;

        /// <summary>
        /// 是否正在加载/保存。
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// 状态栏文案。
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// 供应商搜索文本（业务语义命名）。
        /// </summary>
        public string SearchSupplierText
        {
            get => SearchText;
            set => SearchText = value;
        }

        private string _searchText;
        /// <summary>
        /// 搜索关键字（内部绑定）。
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    DebounceFilter(value);
                }
            }
        }

        /// <summary>
        /// 供应商总数（用于页导航展示）。
        /// </summary>
        public int TotalSupplierCount => AllItems == null ? 0 : AllItems.Count;

        /// <summary>
        /// 供应商分页页码（当前小数据管理不分页，固定返回1以兼容UI绑定）。
        /// </summary>
        public int PageSupplierIndex => 1;

        /// <summary>
        /// 供应商分页页大小（当前小数据管理不分页，返回当前条数）。
        /// </summary>
        public int PageSupplierSize => TotalSupplierCount <= 0 ? 1 : TotalSupplierCount;

        // 业务命名命令别名（便于XAML可读性）
        public RelayCommand AddSupplierCommand { get; }
        public RelayCommand BatchSaveSupplierAsyncCommand { get; }
        public RelayCommand LoadSupplierAsyncCommand { get; }
        public RelayCommand InvertCheckSupplierCommand { get; }
        public RelayCommand DeleteCheckedCommand { get; }

        /// <summary>
        /// 供应商搜索过滤规则。
        /// </summary>
        protected bool FilterItem(SupplierModel item, string searchText)
        {
            return _list.MatchSearch(item, searchText);
        }

        private void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new System.Threading.CancellationTokenSource();

            Task.Delay(350, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                DispatcherProvider.Invoke(() =>
                {
                    ItemsView.Filter = obj => string.IsNullOrWhiteSpace(text) || FilterItem((SupplierModel)obj, text);
                    ItemsView.Refresh();
                });
            }, TaskScheduler.Default);
        }

        private void AddSupplier()
        {
            _list.AddNewOnTop();
            StatusMessage = "已新增一行供应商，请填写后保存。";
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                SearchText = string.Empty;
            }

            RaiseListFacadeProperties();
        }

        private async Task LoadSupplierAsync()
        {
            if (HasChanges)
            {
                var okReload = await _msg.ShowConfirmAsync("当前有未保存的修改，刷新将丢失这些数据，是否继续？");
                if (!okReload) return;
            }

            IsLoading = true;
            StatusMessage = "正在加载供应商数据...";
            try
            {
                var rst = await _myApi.GetAllAsync();
                DispatcherProvider.Invoke(() =>
                {
                    if (rst.Success && rst.Data != null)
                    {
                        _list.ReloadFromDtoList(rst.Data.ToList());
                        DebounceFilter(SearchText);
                        StatusMessage = $"加载成功，共 {TotalSupplierCount} 条供应商。";
                    }
                    else
                    {
                        StatusMessage = "加载失败：" + rst.Message;
                    }

                    RaiseListFacadeProperties();
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "加载异常：" + ex.Message;
            }
            finally
            {
                IsLoading = false;
                RaiseListFacadeProperties();
            }
        }

        private async Task DeleteCheckedAsync()
        {
            if (_list.CheckedCount <= 0)
            {
                await _msg.ShowInfoAsync("请先勾选要删除的供应商行。");
                return;
            }

            var ok = await _msg.ShowConfirmAsync($"确定删除已勾选的 {_list.CheckedCount} 行吗？");
            if (!ok) return;

            var deletedCount = _list.DeleteChecked();
            StatusMessage = $"已标记删除 {deletedCount} 行，点击保存后生效。";
            RaiseListFacadeProperties();
        }

        private async Task<bool> ValidateBeforeSaveAsync(List<SupplierModel> dirtyModels)
        {
            // 1. 必填项空值拦截 (这是替代了以前前端红框的防线)
            var emptyItems = dirtyModels.Where(x => !x.IsValid).ToList();
            if (emptyItems.Any())
            {
                await _msg.ShowWarningAsync("存在未填写完整的行！\n\n【供应商】和【供应商名称】和【单据关联号】为必填项，请补充完整后再保存。");
                return false;
            }
            // 我们把当前 DataGrid 里所有没有被删除的数据拿出来分组
            var activeItems = AllItems.Where(x => x.State != ModelState.Deleted).ToList();

            // ★ 架构师防呆升级：统一转大写 + 去除首尾空格 + 防 Null
            var duplicates = activeItems
                .GroupBy(x => new
                {
                    CustomerCode = x.SupplierCode?.Trim().ToUpper() ?? ""
                })
                .Where(g => g.Count() > 1)
                .ToList(); // 这里不要直接 Select Key，我们把整组数据留下来

            if (duplicates.Any())
            {
                // ★ 体验细节：拿到重复组里的“第一行真实数据”，这样弹窗报错时显示的是用户原来输入的真实大小写
                var firstDupItem = duplicates.First().First();

                await _msg.ShowWarningAsync($"列表中存在重复的配置！\n\n" +
                                  $"供应商代码：{firstDupItem.SupplierCode}\n" +
                                  $"请修改或删除重复行后再保存。");

                // 返回 false 告诉基类：拦截失败！不要发请求！
                return false;
            }

            // 返回 true 告诉基类：校验完美通过，放行！
            return true;
        }

        private async Task BatchSaveSupplierAsync()
        {
            // 抓取脏数据
            var changeSet = _list.GetChangeSet();
            var dirtyModels = _list.Items
                .Where(x => x.State == ModelState.Added || x.State == ModelState.Modified || x.State == ModelState.Deleted)
                .ToList();

            if (!dirtyModels.Any())
            {
                await _msg.ShowInfoAsync("没有需要保存的变动。", "提示");
                return;
            }

            // 保存前校验（删除行不参与必填校验）
            var modelsToValidate = dirtyModels.Where(x => x.State != ModelState.Deleted).ToList();
            if (modelsToValidate.Any(x => !x.IsValid))
            {
                await _msg.ShowWarningAsync("存在未填写完整的数据。", "校验失败");
                return;
            }

            var isValid = await ValidateBeforeSaveAsync(modelsToValidate);
            if (!isValid) return;

            var ok = await _msg.ShowConfirmAsync("确定要保存供应商变更吗？");
            if (!ok) return;

            IsLoading = true;
            StatusMessage = "正在保存供应商变更...";
            try
            {
                var request = new BatchRequestGeneric<SupplierDto>
                {
                    AddList = changeSet.AddList.Select(ToDto).ToList(),
                    UpdateList = changeSet.UpdateList.Select(ToDto).ToList(),
                    DeleteIdList = changeSet.DeleteIdList
                };

                var rst = await _myApi.BatchSaveAsync(request);
                if (rst.Success)
                {
                    await _msg.ShowInfoAsync("保存成功！");
                    await LoadSupplierAsync();
                }
                else
                {
                    await _msg.ShowErrorAsync("保存失败：" + rst.Message);
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("保存异常：" + ex.Message);
            }
            finally
            {
                IsLoading = false;
                RaiseListFacadeProperties();
            }
        }

        private static SupplierDto ToDto(SupplierModel x)
        {
            return new SupplierDto
            {
                Id = x.Id,
                SupplierCode = x.SupplierCode ?? string.Empty,
                SupplierName = x.Name ?? string.Empty,
                DocRelateNo = x.DocRelateNo ?? string.Empty,
                ContactPerson = x.ContactPerson ?? string.Empty,
                ContactPhone = x.ContactPhone ?? string.Empty,
                ContactEmail = x.ContactEmail ?? string.Empty,
                Address = x.Address ?? string.Empty,
                ZheKou = x.ZheKou,
                Currency = x.Currency ?? "CNY",
                TaxRate = x.TaxRate,
                DefaultLeadTime = x.DefaultLeadTime,
                PaymentTerms = x.PaymentTerms ?? string.Empty,
                ZheKouBeiZhu = x.ZheKouBeiZhu ?? string.Empty
            };
        }
    }
}