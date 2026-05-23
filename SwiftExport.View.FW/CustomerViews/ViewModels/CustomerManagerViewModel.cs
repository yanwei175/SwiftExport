using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Models;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.CustomerViews.Enums;
using SwiftExport.View.FW.CustomerViews.Models;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SwiftExport.View.FW.CustomerViews.ViewModels
{
    public class CustomerManagerViewModel : ViewModelBaseEx
    {
        private readonly IDialogService _msg;
        private readonly ICustomerApi _customerApi;

        private readonly DispatcherTimer _搜索客户防抖计时器;

        /// <summary>
        /// 客户产品管理子 ViewModel，负责产品列表的增删改查。
        /// 由 DI 注入后通过 View code-behind 绑定到子视图 DataContext。
        /// </summary>
        public CustomerProductManagerViewModel CustomerProductVM { get; }
  
        public CustomerManagerViewModel(ICustomerApi customerApi, CustomerProductManagerViewModel customerProductViewModel, IDialogService dialogService)
        {
            _customerApi = customerApi;
            _msg = dialogService;
            CustomerProductVM = customerProductViewModel;

            CurrentCustomerTable = new CustomerList();

            PaginationCustomerTableChangedCommand = new RelayCommand<SwiftExport.Contracts.Models.PageChangeRequest>(OnPaginationCustomerTableChanged);

            AddCustomerCommand = new RelayCommand(AddCustomer);
            BatchSaveCustomerAsyncCommand = new RelayCommand(async () => await BatchSaveCustomerAsync(), () => CanBatchSaveCustomerAsync);
            DeleteCustomerAsyncCommand = new RelayCommand(async () => await DeleteCustomerAsync(), () => CanDeleteCustomer);
            LoadCustomerAsyncCommand = new RelayCommand(async () => await LoadCustomerAsync());
            InvertCheckCommand = new RelayCommand(InvertCheck);

            // 监听客户表的状态变化（脏数据、勾选数量等）
            CurrentCustomerTable.ListStateChanged += () =>
            {
                RaisePropertyChanged(nameof(CanBatchSaveCustomerAsync));
                BatchSaveCustomerAsyncCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(CanDeleteCustomer));
                DeleteCustomerAsyncCommand.RaiseCanExecuteChanged();
            };

            _搜索客户防抖计时器 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _搜索客户防抖计时器.Tick += 搜索客户防抖计时器_Tick;

            _ = LoadCustomerAsync();
        }

        #region 字段属性
        private string _statusMessage = "准备就绪";// UI底部的状态提示信息，绑定到UI的状态栏，用于显示当前操作状态、错误信息等，由各个操作方法在执行过程中设置以反馈给用户
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        #region Tab控制相关字段
        private CustomerManagerTabs _activeTab;
        public CustomerManagerTabs ActiveTab
        {
            get => _activeTab;
            set
            {
                if (SetProperty(ref _activeTab, value))
                {
                    // 切换到客户产品 Tab 时，将当前客户同步到子 ViewModel，触发加载
                    if (value == CustomerManagerTabs.CustomerProducts)
                        CustomerProductVM.CurrentCustomer = _selectedCustomer != null ? _selectedCustomer : null;
                    if (value == CustomerManagerTabs.CustomerInfo)
                        //CustomerProductVM.ProductList.Clear();
                        StatusMessage = "";
                }
            }
        }

        #endregion

        #region 客户表字段
        public CustomerList CurrentCustomerTable { get; }
        private CustomerModel _selectedCustomer;
        public CustomerModel SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                if (SetProperty(ref _selectedCustomer, value))
                {
                    // 选中客户变化时，刷新相关按钮可用性
                    RaisePropertyChanged(nameof(HasSelectedCustomer));
                    RaisePropertyChanged(nameof(CanDeleteCustomer));
                    DeleteCustomerAsyncCommand.RaiseCanExecuteChanged();

                    // 客户切换时通知子 ViewModel，子 ViewModel 内部会自动清空并按需加载
                    //CustomerProductVM.CurrentCustomer = value ?? null;
                }
            }
        }

        /// <summary>
        /// 是否选择了客户（用于控制"客户产品管理"标签页的操作按钮可用性，要求已持久化的客户 Id > 0）
        /// </summary>
        public bool HasSelectedCustomer => _selectedCustomer != null && _selectedCustomer.Id > 0;


        private string _searchCustomerText;
        public string SearchCustomerText
        {
            get => _searchCustomerText;
            set
            {
                if (SetProperty(ref _searchCustomerText, value))
                {
                    防抖搜索客户开始();
                }
            }
        }

        #endregion

        #region 客户表分页控件

        private int _pageCustomerTableIndex = 1;// 当前页码，绑定到UI的分页控件，用户切换页码时会触发数据重新加载以显示对应页的数据
        public int PageCustomerTableIndex
        {
            get => _pageCustomerTableIndex;
            set => SetProperty(ref _pageCustomerTableIndex, value);
        }

        private int _pageCustomerTableSize = 50;// 每页行数，绑定到UI的分页控件，用户修改每页行数时会触发数据重新加载以应用新的分页设置
        public int PageCustomerTableSize
        {
            get => _pageCustomerTableSize;
            set => SetProperty(ref _pageCustomerTableSize, value);
        }

        private int _totalCustomerTableCount;// 数据总条数，绑定到UI的分页控件，用于显示总页数和当前数据范围等信息，由数据加载方法在获取数据后设置
        public int TotalCustomerTableCount
        {
            get => _totalCustomerTableCount;
            set => SetProperty(ref _totalCustomerTableCount, value);
        }

        #endregion

        #endregion







        #region 命令
        //==============
        //客户表命令
        //==============
        #region 客户表命令


        //新增客户命令

        public RelayCommand<PageChangeRequest> PaginationCustomerTableChangedCommand { get; }
        private async void OnPaginationCustomerTableChanged(PageChangeRequest request)
        {
            if (CurrentCustomerTable.IsDirty)
            {
                var result = await _msg.ShowConfirmAsync("当前客户表有未保存的数据，翻页将会丢失，是否继续？", "未保存提示");
                if (!result)
                {
                    return;
                }
            }

            PageCustomerTableIndex = request.NewIndex;
            PageCustomerTableSize = request.NewSize;

            await LoadCustomerAsync();
        }

        public RelayCommand AddCustomerCommand { get; }
        private void AddCustomer()
        {
            using (BusyScope())
            {
                var model = new CustomerModel() { CustomerCode = "Newcode" };
                model.MarkAsAdded();
                CurrentCustomerTable.AddModelToFirst(model);
                StatusMessage = "已添加新客户行，请编辑相关信息并保存。";
            }

        }

        /// <summary>
        /// 客户表是否有脏数据
        /// </summary>
        public bool CanBatchSaveCustomerAsync => CurrentCustomerTable.IsDirty;

        //批量保存客户命令
        public RelayCommand BatchSaveCustomerAsyncCommand { get; }
        private async Task BatchSaveCustomerAsync()
        {
            var dirtyModels = CurrentCustomerTable.GetChangeSet();
            if (dirtyModels.AddList.Count == 0 && dirtyModels.UpdateList.Count == 0 && dirtyModels.DeleteIdList.Count == 0)
            {
                StatusMessage = "没有需要保存的更改。";
                return;
            }
            if (CurrentCustomerTable.IsAllValid == false)
            {
                StatusMessage = "请修正数据中的错误后再保存。";
                return;
            }
            StatusMessage = "正在保存更改...";

            var req = dirtyModels.Adapt<BatchRequestGeneric<CustomerDto>>(); // 先调用Adapt方法进行类型转换，确保所有属性都被正确映射
            try
            {
                var result = await _customerApi.BatchSaveAsync(req);
                if (result.Success)
                {
                    StatusMessage = "保存成功。";

                    await _msg.ShowInfoAsync("保存成功！");
                    _ = LoadCustomerAsync();
                }
                else
                {
                    StatusMessage = "保存失败: " + result.Message;
                    return;
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("异常: " + ex.Message);
            }
        }



        /// <summary>
        /// 客户表删除命令是否可用：有勾选项 或 有当前选中行，任意一个满足即可
        /// </summary>
        public bool CanDeleteCustomer => CurrentCustomerTable.CheckedCount > 0 || _selectedCustomer != null;

        public RelayCommand DeleteCustomerAsyncCommand { get; }
        private async Task DeleteCustomerAsync()
        {
            if (SelectedCustomer != null && SelectedCustomer.Id <= 0)
            {
                var tempIndex = CurrentCustomerTable.Items.IndexOf(SelectedCustomer);
                CurrentCustomerTable.Remove(SelectedCustomer);
                CustomerModel temp;
                try
                {
                    temp = CurrentCustomerTable.Items[tempIndex];
                }
                catch
                {
                    temp = CurrentCustomerTable.Items.FirstOrDefault();
                }
                if (temp != null)
                    SelectedCustomer = temp;
            }

            var checkItems = CurrentCustomerTable.GetCheckedItems();

            if (checkItems.Count == 0) return;

            StatusMessage = $"准备删除 {checkItems.Count} 条记录...";
            try
            {
                //删除刚刚新建的行
                var deleteByNew = checkItems.Where(x => x.Id <= 0).ToList();
                foreach (var item in deleteByNew) { CurrentCustomerTable.Remove(item); }

                //发API删除数据库的行
                var apiToDelete = checkItems.Where(x => x.Id > 0).ToList();
                if (apiToDelete.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync($"确定要删除选中的 {apiToDelete.Count} 条记录吗？", "确认删除")) return;

                    bool isSuccess = false;
                    string errMsg = "";

                    if (apiToDelete.Count == 1)
                    {
                        var rst = await _customerApi.DeleteAsync(apiToDelete.First().Id);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    else
                    {
                        var req = new BatchRequestGeneric<CustomerDto> { DeleteIdList = apiToDelete.Select(x => x.Id).ToList() };
                        var rst = await _customerApi.BatchSaveAsync(req);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    if (isSuccess)
                    {
                        foreach (var item in apiToDelete) CurrentCustomerTable.Delete(item);
                        await _msg.ShowInfoAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + errMsg);
                        return;
                    }
                }

            }
            catch (Exception ex) { await _msg.ShowErrorAsync("删除异常：" + ex.Message); }
            finally
            {
                StatusMessage = "准备就绪";
            }
        }
        public RelayCommand LoadCustomerAsyncCommand { get; }
        private async Task LoadCustomerAsync()
        {
            if (_customerApi == null) return;

            using (BusyScope())
            {
                try
                {
                    var requestDto = new PagedRequestDto
                    {
                        BatchKeywords = new List<string> { SearchCustomerText },
                        // 绑定到当前页面索引
                        PageIndex = PageCustomerTableIndex,
                        // 绑定到当前的页面行数(PageCustomerTableSize)
                        PageSize = PageCustomerTableSize
                    };

                    var result = await _customerApi.GetPagedAsync(requestDto);

                    DispatcherProvider.BeginInvoke(() =>
                    {
                        if (result.Success && result.Data != null)
                        {

                            var dtos = result.Data.Items.ToList();

                            CurrentCustomerTable.ReLoadRangeFromDtoList(dtos);

                            TotalCustomerTableCount = result.Data.TotalCount; // 修复：使用实际的总数据条数，而不是当前页查出来的条数
                            StatusMessage = $"加载成功，共 {TotalCustomerTableCount} 条。";
                            return;
                        }
                        else
                        {
                            StatusMessage = "加载失败: " + result.Message;
                        }
                    });
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常: " + ex.Message;
                }
                finally
                {
                }
            }
        }
        //反选命令
        public RelayCommand InvertCheckCommand { get; private set; }
        private void InvertCheck()
        {
            CurrentCustomerTable.InvertCheckAll();
        }

        private void 防抖搜索客户开始()
        {
            _搜索客户防抖计时器.Stop();
            _搜索客户防抖计时器.Start();
        }

        private async void 搜索客户防抖计时器_Tick(object sender, EventArgs e)
        {
            _搜索客户防抖计时器.Stop();

            if (IsBusy) return;

            if (CurrentCustomerTable.IsDirty)
            {
                var result = await _msg.ShowConfirmAsync("当前客户表有未保存的数据，搜索将会丢失，是否继续？", "未保存提示");
                if (!result)
                {
                    return;
                }
            }

            if (PageCustomerTableIndex != 1)
            {
                PageCustomerTableIndex = 1;
            }

            _ = LoadCustomerAsync();
        }



        #endregion

        #endregion








    }
}
